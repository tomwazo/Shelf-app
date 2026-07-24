using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Data.Entities;
using Shelf.Web.Services;

namespace Shelf.Web.Pages.Items;

public class DetailsModel(ShelfDbContext db, ShelfService shelfService) : PageModel
{
    public Item Item { get; private set; } = null!;

    public IReadOnlyList<Event> Timeline { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var item = await db.Items
            .Include(i => i.Genres)
            .SingleOrDefaultAsync(i => i.Id == id, ct);

        if (item is null)
        {
            return NotFound();
        }

        Item = item;

        Timeline = await db.Events
            .Where(e => e.ItemId == id)
            .Include(e => e.User)
            .Include(e => e.Item)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        return Page();
    }

    public async Task<IActionResult> OnPostChangeStatusAsync(int id, ItemStatus status, CancellationToken ct)
    {
        await shelfService.ChangeStatusAsync(id, status, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, CancellationToken ct)
    {
        await shelfService.RemoveItemAsync(id, ct);
        return RedirectToPage(new { id });
    }
}
