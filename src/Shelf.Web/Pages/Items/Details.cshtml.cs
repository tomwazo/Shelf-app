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

    public IReadOnlyList<Comment> Comments { get; private set; } = [];

    public IReadOnlyList<Event> Timeline { get; private set; } = [];

    // Inline-edit convention: a GET query flag switches that section into a
    // prefilled form instead of static text - no separate edit pages, no JS.
    [BindProperty(SupportsGet = true)]
    public string? EditReview { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? EditComment { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditRec { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReviewError { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RecError { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var item = await db.Items
            .Include(i => i.Genres)
            .Include(i => i.Review)
            .SingleOrDefaultAsync(i => i.Id == id, ct);

        if (item is null)
        {
            return NotFound();
        }

        Item = item;

        Comments = await db.Comments
            .Where(c => c.ItemId == id)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

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

    public async Task<IActionResult> OnPostSaveReviewAsync(int id, int? rating, string? text, CancellationToken ct)
    {
        try
        {
            await shelfService.SaveReviewAsync(id, rating, text, ct);
        }
        catch (ArgumentException)
        {
            return RedirectToPage(new { id, editReview = "1", reviewError = "1" });
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteReviewAsync(int id, CancellationToken ct)
    {
        await shelfService.DeleteReviewAsync(id, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddCommentAsync(int id, string text, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            await shelfService.AddCommentAsync(id, text, ct);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostEditCommentAsync(int id, int commentId, string text, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            await shelfService.EditCommentAsync(id, commentId, text, ct);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteCommentAsync(int id, int commentId, CancellationToken ct)
    {
        await shelfService.DeleteCommentAsync(id, commentId, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSaveRecommendationAsync(int id, string note, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return RedirectToPage(new { id, editRec = "1", recError = "1" });
        }

        await shelfService.SaveRecommendationNoteAsync(id, note, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveRecommendationAsync(int id, CancellationToken ct)
    {
        await shelfService.RemoveRecommendationNoteAsync(id, ct);
        return RedirectToPage(new { id });
    }
}
