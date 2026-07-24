using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Data.Entities;
using Shelf.Web.Identity;

namespace Shelf.Web.Services;

// Every mutation pairs a current-state change with an Event append, saved
// together in a single SaveChangesAsync call - that single call IS the
// transactionality guarantee (see TECHNICAL_DESIGN.md § Event log).
public class ShelfService(ShelfDbContext db, ICurrentUserService currentUser)
{
    public async Task<Item> AddOrRestoreItemAsync(
        MediaType mediaType,
        ExternalSource source,
        string externalId,
        string title,
        string coverArtUrl,
        IReadOnlyList<string> genreNames,
        CancellationToken ct = default)
    {
        var existing = await db.Items
            .Include(i => i.Genres)
            .SingleOrDefaultAsync(i => i.ExternalSource == source && i.ExternalId == externalId, ct);

        if (existing is not null && existing.RemovedAt is null)
        {
            // Already active on the Shelf - idempotent, no event.
            return existing;
        }

        var user = await currentUser.GetCurrentUserAsync(ct);

        if (existing is not null)
        {
            // Restore: un-remove, keep prior status/review/comments/note/
            // genres/metadata untouched - never overwrite from posted data.
            existing.RemovedAt = null;
            db.Events.Add(NewEvent(existing, user.Id, EventType.ItemAdded));
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var genres = await ResolveGenresAsync(genreNames, ct);

        var item = new Item
        {
            MediaType = mediaType,
            Title = title,
            CoverArtUrl = coverArtUrl,
            ExternalSource = source,
            ExternalId = externalId,
            Status = ItemStatus.Backlog,
            AddedAt = DateTimeOffset.UtcNow,
            Genres = genres,
        };
        db.Items.Add(item);
        db.Events.Add(NewEvent(item, user.Id, EventType.ItemAdded));
        await db.SaveChangesAsync(ct);

        return item;
    }

    public async Task ChangeStatusAsync(int itemId, ItemStatus newStatus, CancellationToken ct = default)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        if (item.Status == newStatus)
        {
            return;
        }

        var user = await currentUser.GetCurrentUserAsync(ct);
        var fromStatus = item.Status;
        item.Status = newStatus;

        db.Events.Add(NewEvent(item, user.Id, EventType.StatusChanged, fromStatus: fromStatus, toStatus: newStatus));
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveItemAsync(int itemId, CancellationToken ct = default)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        var user = await currentUser.GetCurrentUserAsync(ct);
        item.RemovedAt = DateTimeOffset.UtcNow;

        db.Events.Add(NewEvent(item, user.Id, EventType.ItemRemoved));
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveReviewAsync(int itemId, int? rating, string? text, CancellationToken ct = default)
    {
        if (rating is < 1 or > 5)
        {
            throw new ArgumentException("Rating must be between 1 and 5.", nameof(rating));
        }

        var trimmedText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (rating is null && trimmedText is null)
        {
            throw new ArgumentException("A review needs a rating, text, or both.");
        }

        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        var user = await currentUser.GetCurrentUserAsync(ct);
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.ItemId == itemId, ct);

        EventType eventType;
        if (review is null)
        {
            var now = DateTimeOffset.UtcNow;
            review = new Review
            {
                ItemId = itemId,
                UserId = user.Id,
                Rating = rating,
                Text = trimmedText,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Reviews.Add(review);
            eventType = EventType.ReviewAdded;
        }
        else
        {
            review.Rating = rating;
            review.Text = trimmedText;
            review.UpdatedAt = DateTimeOffset.UtcNow;
            eventType = EventType.ReviewEdited;
        }

        var payload = JsonSerializer.Serialize(new ReviewPayload(rating, trimmedText), EventJson.Options);
        db.Events.Add(NewEvent(item, user.Id, eventType, payload: payload));

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteReviewAsync(int itemId, CancellationToken ct = default)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        var review = await db.Reviews.SingleOrDefaultAsync(r => r.ItemId == itemId, ct);
        if (review is null)
        {
            return;
        }

        var user = await currentUser.GetCurrentUserAsync(ct);
        var payload = JsonSerializer.Serialize(new ReviewPayload(review.Rating, review.Text), EventJson.Options);

        db.Reviews.Remove(review);
        db.Events.Add(NewEvent(item, user.Id, EventType.ReviewDeleted, payload: payload));

        await db.SaveChangesAsync(ct);
    }

    public async Task<Comment> AddCommentAsync(int itemId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Comment text is required.", nameof(text));
        }

        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        var user = await currentUser.GetCurrentUserAsync(ct);
        var trimmedText = text.Trim();
        var now = DateTimeOffset.UtcNow;

        var comment = new Comment
        {
            ItemId = itemId,
            UserId = user.Id,
            Text = trimmedText,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Comments.Add(comment);

        var payload = JsonSerializer.Serialize(new CommentPayload(trimmedText), EventJson.Options);
        db.Events.Add(NewEvent(item, user.Id, EventType.CommentAdded, payload: payload));

        await db.SaveChangesAsync(ct);
        return comment;
    }

    public async Task EditCommentAsync(int itemId, int commentId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Comment text is required.", nameof(text));
        }

        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId && c.ItemId == itemId, ct)
            ?? throw new InvalidOperationException($"Comment {commentId} not found on item {itemId}.");

        var user = await currentUser.GetCurrentUserAsync(ct);
        var trimmedText = text.Trim();
        comment.Text = trimmedText;
        comment.UpdatedAt = DateTimeOffset.UtcNow;

        var payload = JsonSerializer.Serialize(new CommentPayload(trimmedText), EventJson.Options);
        db.Events.Add(NewEvent(item, user.Id, EventType.CommentEdited, payload: payload));

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteCommentAsync(int itemId, int commentId, CancellationToken ct = default)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId && c.ItemId == itemId, ct)
            ?? throw new InvalidOperationException($"Comment {commentId} not found on item {itemId}.");

        var user = await currentUser.GetCurrentUserAsync(ct);
        var payload = JsonSerializer.Serialize(new CommentPayload(comment.Text), EventJson.Options);

        db.Comments.Remove(comment);
        db.Events.Add(NewEvent(item, user.Id, EventType.CommentDeleted, payload: payload));

        await db.SaveChangesAsync(ct);
    }

    public async Task SaveRecommendationNoteAsync(int itemId, string note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("Recommendation note text is required.", nameof(note));
        }

        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        var user = await currentUser.GetCurrentUserAsync(ct);
        var trimmedNote = note.Trim();
        item.RecommendationNote = trimmedNote;

        var payload = JsonSerializer.Serialize(new RecommendationNotePayload(trimmedNote), EventJson.Options);
        db.Events.Add(NewEvent(item, user.Id, EventType.RecommendationNoteChanged, payload: payload));

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveRecommendationNoteAsync(int itemId, CancellationToken ct = default)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");
        EnsureNotRemoved(item);

        if (item.RecommendationNote is null)
        {
            return;
        }

        var user = await currentUser.GetCurrentUserAsync(ct);
        var payload = JsonSerializer.Serialize(new RecommendationNotePayload(item.RecommendationNote), EventJson.Options);
        item.RecommendationNote = null;

        db.Events.Add(NewEvent(item, user.Id, EventType.RecommendationNoteRemoved, payload: payload));
        await db.SaveChangesAsync(ct);
    }

    // Removed items are read-only; the UI hides mutation forms for them,
    // this guard is the backstop.
    private static void EnsureNotRemoved(Item item)
    {
        if (item.RemovedAt is not null)
        {
            throw new InvalidOperationException($"Item {item.Id} has been removed from the Shelf and is read-only.");
        }
    }

    // Matches genres by trimmed name, case-insensitively, entirely in memory
    // rather than relying on the database's collation - SQLite (tests) and
    // SQL Server (prod) don't agree on default string collation, so this
    // keeps genre reuse behavior identical across both.
    private async Task<List<Genre>> ResolveGenresAsync(IReadOnlyList<string> genreNames, CancellationToken ct)
    {
        var trimmedNames = genreNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (trimmedNames.Count == 0)
        {
            return [];
        }

        var existingGenres = await db.Genres.ToListAsync(ct);

        var genres = new List<Genre>();
        foreach (var name in trimmedNames)
        {
            var match = existingGenres.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                genres.Add(match);
            }
            else
            {
                var newGenre = new Genre { Name = name };
                genres.Add(newGenre);
                existingGenres.Add(newGenre); // guard against duplicates within this same call
            }
        }

        return genres;
    }

    private static Event NewEvent(
        Item item, int userId, EventType type,
        ItemStatus? fromStatus = null, ItemStatus? toStatus = null, string? payload = null) => new()
    {
        Item = item,
        UserId = userId,
        OccurredAt = DateTimeOffset.UtcNow,
        Type = type,
        FromStatus = fromStatus,
        ToStatus = toStatus,
        Payload = payload,
    };
}
