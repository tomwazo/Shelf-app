using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Pages;

public class TimelineModel(ShelfDbContext db) : PageModel
{
    private const int MaxEntries = 200;

    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = "7d";

    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    public IReadOnlyList<Event> Events { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var since = Period switch
        {
            "24h" => DateTimeOffset.UtcNow.AddHours(-24),
            "30d" => DateTimeOffset.UtcNow.AddDays(-30),
            "all" => (DateTimeOffset?)null,
            _ => DateTimeOffset.UtcNow.AddDays(-7), // "7d" default
        };

        var query = db.Events.AsQueryable();

        if (since is not null)
        {
            query = query.Where(e => e.OccurredAt >= since);
        }

        if (Type is not null)
        {
            var types = MapTypeFilter(Type);
            query = query.Where(e => types.Contains(e.Type));
        }

        Events = await query
            .Include(e => e.User)
            .Include(e => e.Item)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(MaxEntries)
            .ToListAsync(ct);
    }

    private static EventType[] MapTypeFilter(string type) => type switch
    {
        "added" => [EventType.ItemAdded],
        "removed" => [EventType.ItemRemoved],
        "status" => [EventType.StatusChanged],
        "review" => [EventType.ReviewAdded, EventType.ReviewEdited, EventType.ReviewDeleted],
        "comment" => [EventType.CommentAdded, EventType.CommentEdited, EventType.CommentDeleted],
        "recommendation" => [EventType.RecommendationNoteChanged, EventType.RecommendationNoteRemoved],
        _ => Enum.GetValues<EventType>(),
    };
}
