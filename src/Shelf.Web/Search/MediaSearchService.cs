using Shelf.Web.Data.Entities;

namespace Shelf.Web.Search;

// Top holds up to the top-N results (truncated for the default grouped view);
// TotalCount is the full match count, driving whether "Show more" renders.
// Failed means this type's provider threw - Top is empty, not "no results."
public record MediaTypeSearchResult(IReadOnlyList<MediaSearchResult> Top, int TotalCount, bool Failed);

public class MediaSearchService(IEnumerable<IMediaSearchProvider> providers)
{
    private const int TopResultsPerType = 5;

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct = default)
    {
        var provider = providers.FirstOrDefault(p => p.Supports(mediaType))
            ?? throw new InvalidOperationException($"No search provider registered for media type '{mediaType}'.");

        var results = await provider.SearchAsync(mediaType, query, ct);
        return results.Take(20).ToList();
    }

    // Searches every provider concurrently. A provider that throws only
    // affects the media types it owns - other providers' results still come
    // back, so one flaky external API doesn't blank the whole page.
    public async Task<IReadOnlyDictionary<MediaType, MediaTypeSearchResult>> SearchAllAsync(string query, CancellationToken ct = default)
    {
        var providerAttempts = await Task.WhenAll(providers.Select(async provider =>
        {
            IReadOnlyDictionary<MediaType, IReadOnlyList<MediaSearchResult>> byType;
            var failed = false;
            try
            {
                byType = await provider.SearchAllAsync(query, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                byType = new Dictionary<MediaType, IReadOnlyList<MediaSearchResult>>();
                failed = true;
            }

            return (provider.SupportedTypes, ByType: byType, Failed: failed);
        }));

        var merged = new Dictionary<MediaType, MediaTypeSearchResult>();
        foreach (var attempt in providerAttempts)
        {
            foreach (var type in attempt.SupportedTypes)
            {
                merged[type] = attempt.Failed
                    ? new MediaTypeSearchResult([], 0, Failed: true)
                    : BuildResult(attempt.ByType.GetValueOrDefault(type, []));
            }
        }

        return merged;
    }

    private static MediaTypeSearchResult BuildResult(IReadOnlyList<MediaSearchResult> items) =>
        new(items.Take(TopResultsPerType).ToList(), items.Count, Failed: false);
}
