using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Pages.Items;

// Minimal placeholder: enough to be a real redirect target for the Add flow.
// Status control, remove, review/comments, and the item timeline land in
// later phases.
public class DetailsModel(ShelfDbContext db) : PageModel
{
    public Item Item { get; private set; } = null!;

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
        return Page();
    }
}
