using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data.Entities;
using Shelf.Web.Services;
using Shelf.Web.Tests.TestInfrastructure;

namespace Shelf.Web.Tests.Services;

public class ShelfServiceTests
{
    [Fact]
    public async Task AddItem_New_CreatesBacklogItem_AndItemAddedEvent()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(
            MediaType.Film, ExternalSource.Tmdb, "movie:123", "Heat", "https://example/cover.jpg", ["Crime", "Drama"]);

        Assert.Equal(ItemStatus.Backlog, item.Status);
        Assert.Null(item.RemovedAt);
        Assert.NotEqual(default, item.AddedAt);

        var events = await fixture.Db.Events.Where(e => e.ItemId == item.Id).ToListAsync();
        var addedEvent = Assert.Single(events);
        Assert.Equal(EventType.ItemAdded, addedEvent.Type);
        Assert.Equal(user.Id, addedEvent.UserId);
    }

    [Fact]
    public async Task AddItem_CreatesGenres_AndReusesExistingByName()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", ["Sci-Fi"]);
        await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:2", "Film Two", "", ["sci-fi", "Drama"]);

        var genres = await fixture.Db.Genres.ToListAsync();

        Assert.Equal(2, genres.Count);
        Assert.Contains(genres, g => g.Name == "Sci-Fi");
        Assert.Contains(genres, g => g.Name == "Drama");
    }

    [Fact]
    public async Task AddItem_ActiveDuplicate_ReturnsExistingWithoutNewEvent()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var first = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);
        var second = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One (again)", "", []);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Film One", second.Title); // unchanged - not overwritten by the duplicate call

        var events = await fixture.Db.Events.Where(e => e.ItemId == first.Id).ToListAsync();
        Assert.Single(events);
    }

    [Fact]
    public async Task AddItem_RemovedItem_RestoresRow_KeepsStatusAndReview_LogsItemAdded()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);

        var existingItem = new Item
        {
            MediaType = MediaType.Film,
            Title = "Old Title",
            CoverArtUrl = "",
            ExternalSource = ExternalSource.Tmdb,
            ExternalId = "movie:1",
            Status = ItemStatus.Finished,
            AddedAt = DateTimeOffset.UtcNow.AddDays(-30),
            RemovedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        fixture.Db.Items.Add(existingItem);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.Reviews.Add(new Review
        {
            ItemId = existingItem.Id,
            UserId = user.Id,
            Rating = 5,
            Text = "Loved it",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        await fixture.Db.SaveChangesAsync();

        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var restored = await service.AddOrRestoreItemAsync(
            MediaType.Film, ExternalSource.Tmdb, "movie:1", "New Title From Search", "https://new-cover", []);

        Assert.Equal(existingItem.Id, restored.Id);
        Assert.Null(restored.RemovedAt);
        Assert.Equal(ItemStatus.Finished, restored.Status);
        Assert.Equal("Old Title", restored.Title);

        var review = await fixture.Db.Reviews.SingleOrDefaultAsync(r => r.ItemId == existingItem.Id);
        Assert.NotNull(review);
        Assert.Equal("Loved it", review!.Text);

        var events = await fixture.Db.Events.Where(e => e.ItemId == existingItem.Id).ToListAsync();
        var addedEvent = Assert.Single(events);
        Assert.Equal(EventType.ItemAdded, addedEvent.Type);
    }

    [Fact]
    public async Task ChangeStatus_LogsEventWithFromAndToColumns()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);

        await service.ChangeStatusAsync(item.Id, ItemStatus.Finished);

        var updated = await fixture.Db.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(ItemStatus.Finished, updated.Status);

        var statusEvent = await fixture.Db.Events.SingleAsync(e => e.ItemId == item.Id && e.Type == EventType.StatusChanged);
        Assert.Equal(ItemStatus.Backlog, statusEvent.FromStatus);
        Assert.Equal(ItemStatus.Finished, statusEvent.ToStatus);
    }

    [Fact]
    public async Task ChangeStatus_SameStatus_NoEvent()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);

        await service.ChangeStatusAsync(item.Id, ItemStatus.Backlog);

        var events = await fixture.Db.Events.Where(e => e.ItemId == item.Id).ToListAsync();
        Assert.Single(events); // only the original ItemAdded - no StatusChanged event
    }

    [Fact]
    public async Task RemoveItem_SetsRemovedAt_AndLogsItemRemoved()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);

        await service.RemoveItemAsync(item.Id);

        var updated = await fixture.Db.Items.SingleAsync(i => i.Id == item.Id);
        Assert.NotNull(updated.RemovedAt);

        var removedEvent = await fixture.Db.Events.SingleAsync(e => e.ItemId == item.Id && e.Type == EventType.ItemRemoved);
        Assert.Equal(user.Id, removedEvent.UserId);
    }

    [Fact]
    public async Task Mutation_OnRemovedItem_Throws()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);
        await service.RemoveItemAsync(item.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeStatusAsync(item.Id, ItemStatus.Finished));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveItemAsync(item.Id));
    }

    [Fact]
    public async Task SaveReview_New_InsertsAndLogsReviewAddedSnapshot()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);

        await service.SaveReviewAsync(item.Id, 4, "Pretty good");

        var review = await fixture.Db.Reviews.SingleAsync(r => r.ItemId == item.Id);
        Assert.Equal(4, review.Rating);
        Assert.Equal("Pretty good", review.Text);
        Assert.Equal(review.CreatedAt, review.UpdatedAt);

        var addedEvent = await fixture.Db.Events.SingleAsync(e => e.ItemId == item.Id && e.Type == EventType.ReviewAdded);
        var payload = JsonSerializer.Deserialize<ReviewPayload>(addedEvent.Payload!, EventJson.Options);
        Assert.Equal(4, payload!.Rating);
        Assert.Equal("Pretty good", payload.Text);
    }

    [Fact]
    public async Task SaveReview_Existing_UpdatesAndLogsReviewEditedSnapshot()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);
        await service.SaveReviewAsync(item.Id, 3, "Okay");

        await service.SaveReviewAsync(item.Id, 5, "Actually great");

        var review = await fixture.Db.Reviews.SingleAsync(r => r.ItemId == item.Id);
        Assert.Equal(5, review.Rating);
        Assert.Equal("Actually great", review.Text);
        Assert.NotEqual(review.CreatedAt, review.UpdatedAt);

        var editedEvent = await fixture.Db.Events.SingleAsync(e => e.ItemId == item.Id && e.Type == EventType.ReviewEdited);
        var payload = JsonSerializer.Deserialize<ReviewPayload>(editedEvent.Payload!, EventJson.Options);
        Assert.Equal(5, payload!.Rating);
        Assert.Equal("Actually great", payload.Text);
    }

    [Fact]
    public async Task DeleteReview_HardDeletes_AndLogsLastKnownSnapshot()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);
        await service.SaveReviewAsync(item.Id, 2, "Meh");

        await service.DeleteReviewAsync(item.Id);

        Assert.Empty(await fixture.Db.Reviews.Where(r => r.ItemId == item.Id).ToListAsync());

        var deletedEvent = await fixture.Db.Events.SingleAsync(e => e.ItemId == item.Id && e.Type == EventType.ReviewDeleted);
        var payload = JsonSerializer.Deserialize<ReviewPayload>(deletedEvent.Payload!, EventJson.Options);
        Assert.Equal(2, payload!.Rating);
        Assert.Equal("Meh", payload.Text);
    }

    [Fact]
    public async Task SaveReview_AfterDelete_NewRowWithNewCreatedAt()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);
        await service.SaveReviewAsync(item.Id, 2, "First take");
        var firstReview = await fixture.Db.Reviews.SingleAsync(r => r.ItemId == item.Id);
        var firstCreatedAt = firstReview.CreatedAt;
        var firstId = firstReview.Id;

        await service.DeleteReviewAsync(item.Id);
        await service.SaveReviewAsync(item.Id, 5, "Second take");

        var secondReview = await fixture.Db.Reviews.SingleAsync(r => r.ItemId == item.Id);
        Assert.NotEqual(firstId, secondReview.Id);
        Assert.True(secondReview.CreatedAt >= firstCreatedAt);
        Assert.Equal("Second take", secondReview.Text);
    }

    [Fact]
    public async Task SaveReview_EmptyRatingAndText_Throws()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveReviewAsync(item.Id, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveReviewAsync(item.Id, null, "   "));
    }

    [Fact]
    public async Task DeleteComment_LogsLastKnownTextWithCommentId()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var service = new ShelfService(fixture.Db, new StubCurrentUserService(user));

        var item = await service.AddOrRestoreItemAsync(MediaType.Film, ExternalSource.Tmdb, "movie:1", "Film One", "", []);
        var comment = await service.AddCommentAsync(item.Id, "Great scene");

        await service.DeleteCommentAsync(item.Id, comment.Id);

        Assert.Empty(await fixture.Db.Comments.Where(c => c.ItemId == item.Id).ToListAsync());

        var deletedEvent = await fixture.Db.Events.SingleAsync(e => e.ItemId == item.Id && e.Type == EventType.CommentDeleted);
        var payload = JsonSerializer.Deserialize<CommentPayload>(deletedEvent.Payload!, EventJson.Options);
        Assert.Equal("Great scene", payload!.Text);
    }
}
