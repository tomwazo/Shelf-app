using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Data.Entities;
using Shelf.Web.Services;

namespace Shelf.Web.Pages;

public class ShelfModel(ShelfDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public MediaType? Type { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? GenreId { get; set; }

    [BindProperty(SupportsGet = true)]
    public ItemStatus? Status { get; set; }

    // "yes" = consumed (InProgress or Finished), "no" = not consumed (Backlog), null = no filter.
    [BindProperty(SupportsGet = true)]
    public string? Consumed { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "added";

    public IReadOnlyList<Item> Items { get; private set; } = [];

    public IReadOnlyList<Genre> AvailableGenres { get; private set; } = [];

    public string CountText { get; private set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        AvailableGenres = await db.Genres
            .Where(g => g.Items.Any(i => i.RemovedAt == null))
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        var query = db.Items.Where(i => i.RemovedAt == null);

        if (Type is not null)
        {
            query = query.Where(i => i.MediaType == Type);
        }

        if (GenreId is not null)
        {
            query = query.Where(i => i.Genres.Any(g => g.Id == GenreId));
        }

        if (Status is not null)
        {
            query = query.Where(i => i.Status == Status);
        }

        if (Consumed == "yes")
        {
            query = query.Where(i => i.Status == ItemStatus.InProgress || i.Status == ItemStatus.Finished);
        }
        else if (Consumed == "no")
        {
            query = query.Where(i => i.Status == ItemStatus.Backlog);
        }

        query = Sort switch
        {
            "az" => query.OrderBy(i => i.Title),
            "za" => query.OrderByDescending(i => i.Title),
            _ => query.OrderByDescending(i => i.AddedAt),
        };

        Items = await query.ToListAsync(ct);
        CountText = BuildCountText(Items.Count, Type);
    }

    private static string BuildCountText(int count, MediaType? type)
    {
        if (type is not null)
        {
            return count == 1
                ? $"1 {Display.MediaTypeSingular(type.Value)}"
                : $"{count} {Display.MediaTypePlural(type.Value)}";
        }

        return count == 1 ? "1 item" : $"{count} items";
    }
}
