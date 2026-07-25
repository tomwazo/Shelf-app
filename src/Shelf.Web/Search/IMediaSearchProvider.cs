using Shelf.Web.Data.Entities;

namespace Shelf.Web.Search;

// "" CoverArtUrl means the provider had no cover for this result.
public record MediaSearchResult(
    ExternalSource Source,
    string ExternalId,
    string Title,
    string? Year,
    string CoverArtUrl,
    IReadOnlyList<string> Genres);

public interface IMediaSearchProvider
{
    bool Supports(MediaType mediaType);

    IReadOnlyList<MediaType> SupportedTypes { get; }

    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct = default);

    // Searches across all of this provider's SupportedTypes at once. Providers
    // backed by a single external endpoint per type (TMDB) should implement
    // this to avoid redundant calls, rather than calling SearchAsync once per type.
    Task<IReadOnlyDictionary<MediaType, IReadOnlyList<MediaSearchResult>>> SearchAllAsync(string query, CancellationToken ct = default);
}
