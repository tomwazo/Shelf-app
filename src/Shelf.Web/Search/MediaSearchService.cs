using Shelf.Web.Data.Entities;

namespace Shelf.Web.Search;

public class MediaSearchService(IEnumerable<IMediaSearchProvider> providers)
{
    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct = default)
    {
        var provider = providers.FirstOrDefault(p => p.Supports(mediaType))
            ?? throw new InvalidOperationException($"No search provider registered for media type '{mediaType}'.");

        var results = await provider.SearchAsync(mediaType, query, ct);
        return results.Take(20).ToList();
    }
}
