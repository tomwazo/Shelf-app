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

    private static Event NewEvent(Item item, int userId, EventType type) => new()
    {
        Item = item,
        UserId = userId,
        OccurredAt = DateTimeOffset.UtcNow,
        Type = type,
    };
}
