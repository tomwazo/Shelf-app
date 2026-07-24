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

    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct = default);
}
