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

    public IReadOnlyList<MediaTypeSection> Sections { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public bool HasSearched => !string.IsNullOrWhiteSpace(Q);

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!HasSearched)
        {
            return;
        }

        var query = Q!.Trim();

        if (MediaType is null)
        {
            await SearchAllTypesAsync(query, ct);
        }
        else
        {
            await SearchSingleTypeAsync(MediaType.Value, query, ct);
        }
    }

    private async Task SearchSingleTypeAsync(MediaType mediaType, string query, CancellationToken ct)
    {
        IReadOnlyList<MediaSearchResult> searchResults;
        try
        {
            searchResults = await searchService.SearchAsync(mediaType, query, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ErrorMessage = "Search is unavailable right now — try again.";
            return;
        }

        Results = await BuildRowsAsync(searchResults, ct);
    }

    private async Task SearchAllTypesAsync(string query, CancellationToken ct)
    {
        var byType = await searchService.SearchAllAsync(query, ct);

        var allResults = byType.Values.SelectMany(t => t.Top).ToList();
        var onShelfLookup = await BuildOnShelfLookupAsync(allResults, ct);

        Sections = Enum.GetValues<Data.Entities.MediaType>()
            .Where(type => byType.TryGetValue(type, out var result) && (result.TotalCount > 0 || result.Failed))
            .Select(type =>
            {
                var result = byType[type];
                var rows = result.Top
                    .Select(r => new AddResultRow(r, onShelfLookup.TryGetValue((r.Source, r.ExternalId), out var id) ? id : null))
                    .ToList();

                return new MediaTypeSection(
                    type,
                    rows,
                    result.TotalCount,
                    result.Failed,
                    ShowMoreUrl: result.TotalCount > rows.Count
                        ? Url.Page("/Add", new { MediaType = type, Q = query })
                        : null);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<AddResultRow>> BuildRowsAsync(IReadOnlyList<MediaSearchResult> searchResults, CancellationToken ct)
    {
        if (searchResults.Count == 0)
        {
            return [];
        }

        var onShelfLookup = await BuildOnShelfLookupAsync(searchResults, ct);

        return searchResults
            .Select(r => new AddResultRow(r, onShelfLookup.TryGetValue((r.Source, r.ExternalId), out var itemId) ? itemId : null))
            .ToList();
    }

    private async Task<IReadOnlyDictionary<(ExternalSource, string), int>> BuildOnShelfLookupAsync(IReadOnlyList<MediaSearchResult> searchResults, CancellationToken ct)
    {
        if (searchResults.Count == 0)
        {
            return new Dictionary<(ExternalSource, string), int>();
        }

        var sources = searchResults.Select(r => r.Source).Distinct().ToList();
        var externalIds = searchResults.Select(r => r.ExternalId).ToList();

        var onShelf = await db.Items
            .Where(i => sources.Contains(i.ExternalSource) && externalIds.Contains(i.ExternalId) && i.RemovedAt == null)
            .Select(i => new { i.Id, i.ExternalSource, i.ExternalId })
            .ToListAsync(ct);

        return onShelf.ToDictionary(x => (x.ExternalSource, x.ExternalId), x => x.Id);
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        var item = await shelfService.AddOrRestoreItemAsync(
            Input.MediaType, Input.Source, Input.ExternalId, Input.Title, Input.CoverArtUrl, Input.Genres, ct);

        return RedirectToPage("/Items/Details", new { id = item.Id });
    }

    public record AddResultRow(MediaSearchResult Result, int? ExistingItemId);

    public record MediaTypeSection(
        MediaType Type,
        IReadOnlyList<AddResultRow> Rows,
        int TotalCount,
        bool Failed,
        string? ShowMoreUrl);

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
