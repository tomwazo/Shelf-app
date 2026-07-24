using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Data.Entities;
using Shelf.Web.Search;
using Shelf.Web.Services;

namespace Shelf.Web.Pages;

public class AddModel(MediaSearchService searchService, ShelfDbContext db, ShelfService shelfService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public MediaType? MediaType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public AddItemInput Input { get; set; } = new();

    public IReadOnlyList<AddResultRow> Results { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public bool HasSearched => MediaType is not null && !string.IsNullOrWhiteSpace(Q);

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!HasSearched)
        {
            return;
        }

        IReadOnlyList<MediaSearchResult> searchResults;
        try
        {
            searchResults = await searchService.SearchAsync(MediaType!.Value, Q!.Trim(), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ErrorMessage = "Search is unavailable right now — try again.";
            return;
        }

        if (searchResults.Count == 0)
        {
            return;
        }

        var sources = searchResults.Select(r => r.Source).Distinct().ToList();
        var externalIds = searchResults.Select(r => r.ExternalId).ToList();

        var onShelf = await db.Items
            .Where(i => sources.Contains(i.ExternalSource) && externalIds.Contains(i.ExternalId) && i.RemovedAt == null)
            .Select(i => new { i.Id, i.ExternalSource, i.ExternalId })
            .ToListAsync(ct);

        var onShelfLookup = onShelf.ToDictionary(x => (x.ExternalSource, x.ExternalId), x => x.Id);

        Results = searchResults
            .Select(r => new AddResultRow(
                r,
                onShelfLookup.TryGetValue((r.Source, r.ExternalId), out var itemId) ? itemId : null))
            .ToList();
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        var item = await shelfService.AddOrRestoreItemAsync(
            Input.MediaType, Input.Source, Input.ExternalId, Input.Title, Input.CoverArtUrl, Input.Genres, ct);

        return RedirectToPage("/Items/Details", new { id = item.Id });
    }

    public record AddResultRow(MediaSearchResult Result, int? ExistingItemId);

    public class AddItemInput
    {
        public MediaType MediaType { get; set; }
        public ExternalSource Source { get; set; }
        public string ExternalId { get; set; } = "";
        public string Title { get; set; } = "";
        public string CoverArtUrl { get; set; } = "";
        public List<string> Genres { get; set; } = [];
    }
}
