using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Services;

public record StatsRow(MediaType MediaType, int InProgress, int Finished);

public record StatsItem(
    int ItemId, string Title, string CoverArtUrl,
    MediaType MediaType, ItemStatus FurthestStatus, DateTimeOffset LatestActivityAt);

public record StatsResult(int Total, IReadOnlyList<StatsRow> ByMediaType, IReadOnlyList<StatsItem> Items);

public class StatsService(ShelfDbContext db)
{
    // since = null means all time. Removed items are included - the
    // activity happened, and their detail pages still render.
    public async Task<StatsResult> GetStatsAsync(DateTimeOffset? since, CancellationToken ct = default)
    {
        var query = db.Events.Where(e =>
            e.Type == EventType.StatusChanged &&
            (e.ToStatus == ItemStatus.InProgress || e.ToStatus == ItemStatus.Finished));

        if (since is not null)
        {
            query = query.Where(e => e.OccurredAt >= since);
        }

        var relevantEvents = await query.Include(e => e.Item).ToListAsync(ct);

        // Dedupe per item, in memory (fine at this app's scale): Finished
        // always beats InProgress within the window, regardless of the
        // order the transitions happened in.
        var items = relevantEvents
            .GroupBy(e => e.ItemId)
            .Select(g =>
            {
                var furthest = g.Any(e => e.ToStatus == ItemStatus.Finished) ? ItemStatus.Finished : ItemStatus.InProgress;
                var item = g.First().Item;
                return new StatsItem(item.Id, item.Title, item.CoverArtUrl, item.MediaType, furthest, g.Max(e => e.OccurredAt));
            })
            .OrderByDescending(i => i.LatestActivityAt)
            .ToList();

        var byMediaType = items
            .GroupBy(i => i.MediaType)
            .Select(g => new StatsRow(
                g.Key,
                g.Count(i => i.FurthestStatus == ItemStatus.InProgress),
                g.Count(i => i.FurthestStatus == ItemStatus.Finished)))
            .OrderBy(r => r.MediaType)
            .ToList();

        return new StatsResult(items.Count, byMediaType, items);
    }
}
