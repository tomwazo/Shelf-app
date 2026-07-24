using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Search.Providers;

public class OpenLibrarySearchProvider(HttpClient http) : IMediaSearchProvider
{
    private const string WorksKeyPrefix = "/works/";

    public bool Supports(MediaType mediaType) => mediaType == MediaType.Book;

    public IReadOnlyList<MediaType> SupportedTypes => [MediaType.Book];

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct = default)
    {
        var url = $"search.json?q={Uri.EscapeDataString(query)}&limit=20&fields=key,title,first_publish_year,cover_i,subject";
        var response = await http.GetFromJsonAsync<OpenLibrarySearchResponse>(url, ct);
        return (response?.Docs ?? []).Select(Map).ToList();
    }

    public async Task<IReadOnlyDictionary<MediaType, IReadOnlyList<MediaSearchResult>>> SearchAllAsync(string query, CancellationToken ct = default)
    {
        var results = await SearchAsync(MediaType.Book, query, ct);
        return new Dictionary<MediaType, IReadOnlyList<MediaSearchResult>> { [MediaType.Book] = results };
    }

    private static MediaSearchResult Map(OpenLibraryDoc doc)
    {
        var externalId = doc.Key?.StartsWith(WorksKeyPrefix, StringComparison.Ordinal) == true
            ? doc.Key[WorksKeyPrefix.Length..]
            : doc.Key ?? "";

        var cover = doc.CoverId.HasValue
            ? $"https://covers.openlibrary.org/b/id/{doc.CoverId}-M.jpg"
            : "";

        // OpenLibrary's "subject" list is noisy free text, not a curated
        // genre taxonomy - keep only the first few short entries.
        var genres = (doc.Subject ?? [])
            .Where(s => s.Length <= 40)
            .Take(5)
            .ToList();

        return new MediaSearchResult(
            ExternalSource.OpenLibrary,
            externalId,
            doc.Title ?? "(untitled)",
            doc.FirstPublishYear?.ToString(),
            cover,
            genres);
    }

    private sealed record OpenLibrarySearchResponse
    {
        [JsonPropertyName("docs")]
        public List<OpenLibraryDoc> Docs { get; init; } = [];
    }

    private sealed record OpenLibraryDoc
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("first_publish_year")]
        public int? FirstPublishYear { get; init; }

        [JsonPropertyName("cover_i")]
        public int? CoverId { get; init; }

        [JsonPropertyName("subject")]
        public List<string>? Subject { get; init; }
    }
}
