using Shelf.Web.Data;
using Shelf.Web.Data.Entities;
using Shelf.Web.Services;
using Shelf.Web.Tests.TestInfrastructure;

namespace Shelf.Web.Tests.Services;

public class StatsServiceTests
{
    [Fact]
    public async Task Stats_FinishedInWindow_CountsAsFinished()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var item = await SeedItemAsync(fixture.Db, MediaType.Film);
        await SeedStatusEventAsync(fixture.Db, item, user, ItemStatus.Backlog, ItemStatus.Finished, DateTimeOffset.UtcNow.AddDays(-1));

        var result = await new StatsService(fixture.Db).GetStatsAsync(DateTimeOffset.UtcNow.AddDays(-7));

        Assert.Equal(1, result.Total);
        Assert.Equal(ItemStatus.Finished, Assert.Single(result.Items).FurthestStatus);
    }

    [Fact]
    public async Task Stats_InProgressThenFinishedInWindow_CountsOnce_AsFinished()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var item = await SeedItemAsync(fixture.Db, MediaType.Book);
        var now = DateTimeOffset.UtcNow;
        await SeedStatusEventAsync(fixture.Db, item, user, ItemStatus.Backlog, ItemStatus.InProgress, now.AddDays(-3));
        await SeedStatusEventAsync(fixture.Db, item, user, ItemStatus.InProgress, ItemStatus.Finished, now.AddDays(-1));

        var result = await new StatsService(fixture.Db).GetStatsAsync(now.AddDays(-7));

        Assert.Equal(1, result.Total);
        Assert.Equal(ItemStatus.Finished, result.Items.Single().FurthestStatus);
    }

    [Fact]
    public async Task Stats_FinishedThenBackToInProgress_StillCountsAsFinished()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var item = await SeedItemAsync(fixture.Db, MediaType.Game);
        var now = DateTimeOffset.UtcNow;
        await SeedStatusEventAsync(fixture.Db, item, user, ItemStatus.InProgress, ItemStatus.Finished, now.AddDays(-3));
        await SeedStatusEventAsync(fixture.Db, item, user, ItemStatus.Finished, ItemStatus.InProgress, now.AddDays(-1));

        var result = await new StatsService(fixture.Db).GetStatsAsync(now.AddDays(-7));

        Assert.Equal(ItemStatus.Finished, result.Items.Single().FurthestStatus);
    }

    [Fact]
    public async Task Stats_ChangeBeforeWindow_Excluded()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var item = await SeedItemAsync(fixture.Db, MediaType.Film);
        var now = DateTimeOffset.UtcNow;
        await SeedStatusEventAsync(fixture.Db, item, user, ItemStatus.Backlog, ItemStatus.Finished, now.AddDays(-30));

        var result = await new StatsService(fixture.Db).GetStatsAsync(now.AddDays(-7));

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Stats_BreaksDownByMediaType()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var now = DateTimeOffset.UtcNow;

        var film = await SeedItemAsync(fixture.Db, MediaType.Film);
        await SeedStatusEventAsync(fixture.Db, film, user, ItemStatus.Backlog, ItemStatus.Finished, now.AddDays(-1));

        var book = await SeedItemAsync(fixture.Db, MediaType.Book);
        await SeedStatusEventAsync(fixture.Db, book, user, ItemStatus.Backlog, ItemStatus.InProgress, now.AddDays(-1));

        var result = await new StatsService(fixture.Db).GetStatsAsync(now.AddDays(-7));

        Assert.Equal(2, result.Total);
        var filmRow = result.ByMediaType.Single(r => r.MediaType == MediaType.Film);
        Assert.Equal(0, filmRow.InProgress);
        Assert.Equal(1, filmRow.Finished);
        var bookRow = result.ByMediaType.Single(r => r.MediaType == MediaType.Book);
        Assert.Equal(1, bookRow.InProgress);
        Assert.Equal(0, bookRow.Finished);
    }

    [Fact]
    public async Task Stats_IncludesRemovedItems()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var item = await SeedItemAsync(fixture.Db, MediaType.Film, removed: true);
        await SeedStatusEventAsync(fixture.Db, item, user, ItemStatus.Backlog, ItemStatus.Finished, DateTimeOffset.UtcNow.AddDays(-1));

        var result = await new StatsService(fixture.Db).GetStatsAsync(DateTimeOffset.UtcNow.AddDays(-7));

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task Stats_AllTime_IncludesEverything()
    {
        using var fixture = new SqliteDbFixture();
        var user = await TestUsers.SeedAsync(fixture.Db);
        var item = await SeedItemAsync(fixture.Db, MediaType.Film);
        await SeedStatusEventAsync(fixture.Db, item, user, ItemStatus.Backlog, ItemStatus.Finished, DateTimeOffset.UtcNow.AddYears(-2));

        var result = await new StatsService(fixture.Db).GetStatsAsync(since: null);

        Assert.Equal(1, result.Total);
    }

    private static async Task<Item> SeedItemAsync(ShelfDbContext db, MediaType mediaType, bool removed = false)
    {
        var item = new Item
        {
            MediaType = mediaType,
            Title = $"Item {Guid.NewGuid()}",
            CoverArtUrl = "",
            ExternalSource = ExternalSource.Tmdb,
            ExternalId = Guid.NewGuid().ToString(),
            Status = ItemStatus.Backlog,
            AddedAt = DateTimeOffset.UtcNow.AddDays(-60),
            RemovedAt = removed ? DateTimeOffset.UtcNow.AddDays(-1) : null,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task SeedStatusEventAsync(
        ShelfDbContext db, Item item, User user, ItemStatus from, ItemStatus to, DateTimeOffset occurredAt)
    {
        db.Events.Add(new Event
        {
            Item = item,
            UserId = user.Id,
            OccurredAt = occurredAt,
            Type = EventType.StatusChanged,
            FromStatus = from,
            ToStatus = to,
        });
        await db.SaveChangesAsync();
    }
}
