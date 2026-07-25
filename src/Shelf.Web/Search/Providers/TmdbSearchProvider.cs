using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Search.Providers;

// TMDB treats "documentary" as a genre, not a media type, and documentaries
// exist as both movies and series - so a Documentary search queries both
// endpoints and merges the results, each labeled with its underlying kind.
public class TmdbSearchProvider(HttpClient http, IConfiguration configuration, IMemoryCache cache) : IMediaSearchProvider
{
    private const string ImageBaseUrl = "https://image.tmdb.org/t/p/w342";

    public bool Supports(MediaType mediaType) =>
        mediaType is MediaType.Tv or MediaType.Film or MediaType.Documentary;

    public IReadOnlyList<MediaType> SupportedTypes => [MediaType.Tv, MediaType.Film, MediaType.Documentary];

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct = default)
    {
        var apiKey = GetApiKey();
        var results = new List<MediaSearchResult>();
        var isDocumentary = mediaType == MediaType.Documentary;

        if (mediaType is MediaType.Film or MediaType.Documentary)
        {
            var (movies, genreMap) = await FetchMoviesAsync(apiKey, query, ct);
            results.AddRange(movies.Select(r => MapMovie(r, genreMap, isDocumentary ? "(Film)" : null)));
        }

        if (mediaType is MediaType.Tv or MediaType.Documentary)
        {
            var (tv, genreMap) = await FetchTvAsync(apiKey, query, ct);
            results.AddRange(tv.Select(r => MapTv(r, genreMap, isDocumentary ? "(TV series)" : null)));
        }

        return results;
    }

    // Fetches the movie and tv endpoints once each and builds all three
    // supported types from those two calls, rather than calling SearchAsync
    // per type (which would hit movie/tv search twice each for Documentary).
    public async Task<IReadOnlyDictionary<MediaType, IReadOnlyList<MediaSearchResult>>> SearchAllAsync(string query, CancellationToken ct = default)
    {
        var apiKey = GetApiKey();

        var (movies, movieGenreMap) = await FetchMoviesAsync(apiKey, query, ct);
        var (tv, tvGenreMap) = await FetchTvAsync(apiKey, query, ct);

        IReadOnlyList<MediaSearchResult> filmResults = movies.Select(r => MapMovie(r, movieGenreMap, null)).ToList();
        IReadOnlyList<MediaSearchResult> tvResults = tv.Select(r => MapTv(r, tvGenreMap, null)).ToList();
        IReadOnlyList<MediaSearchResult> documentaryResults = movies.Select(r => MapMovie(r, movieGenreMap, "(Film)"))
            .Concat(tv.Select(r => MapTv(r, tvGenreMap, "(TV series)")))
            .ToList();

        return new Dictionary<MediaType, IReadOnlyList<MediaSearchResult>>
        {
            [MediaType.Film] = filmResults,
            [MediaType.Tv] = tvResults,
            [MediaType.Documentary] = documentaryResults,
        };
    }

    private string GetApiKey() =>
        configuration["Tmdb:ApiKey"] ?? throw new InvalidOperationException("Missing configuration 'Tmdb:ApiKey'");

    private async Task<(List<TmdbResult> Items, IReadOnlyDictionary<int, string> GenreMap)> FetchMoviesAsync(string apiKey, string query, CancellationToken ct)
    {
        var genreMap = await GetGenreMapAsync("movie", apiKey, ct);
        var movies = await SearchEndpointAsync("search/movie", apiKey, query, ct);
        return (movies, genreMap);
    }

    private async Task<(List<TmdbResult> Items, IReadOnlyDictionary<int, string> GenreMap)> FetchTvAsync(string apiKey, string query, CancellationToken ct)
    {
        var genreMap = await GetGenreMapAsync("tv", apiKey, ct);
        var tv = await SearchEndpointAsync("search/tv", apiKey, query, ct);
        return (tv, genreMap);
    }

    private async Task<List<TmdbResult>> SearchEndpointAsync(string endpoint, string apiKey, string query, CancellationToken ct)
    {
        var url = $"{endpoint}?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query)}";
        var response = await http.GetFromJsonAsync<TmdbSearchResponse>(url, ct);
        return response?.Results ?? [];
    }

    private async Task<IReadOnlyDictionary<int, string>> GetGenreMapAsync(string mediaKind, string apiKey, CancellationToken ct)
    {
        var cacheKey = $"tmdb-genres-{mediaKind}";
        if (cache.TryGetValue(cacheKey, out Dictionary<int, string>? cached) && cached is not null)
        {
            return cached;
        }

        var url = $"genre/{mediaKind}/list?api_key={Uri.EscapeDataString(apiKey)}";
        var response = await http.GetFromJsonAsync<TmdbGenreListResponse>(url, ct);
        var map = (response?.Genres ?? []).ToDictionary(g => g.Id, g => g.Name);

        cache.Set(cacheKey, map, TimeSpan.FromHours(24));
        return map;
    }

    private static MediaSearchResult MapMovie(TmdbResult r, IReadOnlyDictionary<int, string> genreMap, string? label) =>
        new(
            ExternalSource.Tmdb,
            $"movie:{r.Id}",
            r.Title ?? "(untitled)",
            CombineYearLabel(ExtractYear(r.ReleaseDate), label),
            string.IsNullOrEmpty(r.PosterPath) ? "" : $"{ImageBaseUrl}{r.PosterPath}",
            MapGenres(r.GenreIds, genreMap));

    private static MediaSearchResult MapTv(TmdbResult r, IReadOnlyDictionary<int, string> genreMap, string? label) =>
        new(
            ExternalSource.Tmdb,
            $"tv:{r.Id}",
            r.Name ?? "(untitled)",
            CombineYearLabel(ExtractYear(r.FirstAirDate), label),
            string.IsNullOrEmpty(r.PosterPath) ? "" : $"{ImageBaseUrl}{r.PosterPath}",
            MapGenres(r.GenreIds, genreMap));

    private static List<string> MapGenres(List<int>? genreIds, IReadOnlyDictionary<int, string> genreMap) =>
        (genreIds ?? [])
            .Select(id => genreMap.GetValueOrDefault(id))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();

    private static string? ExtractYear(string? date) =>
        string.IsNullOrEmpty(date) || date.Length < 4 ? null : date[..4];

    private static string? CombineYearLabel(string? year, string? label) => (year, label) switch
    {
        (null, null) => null,
        (not null, null) => year,
        (null, not null) => label,
        _ => $"{year} {label}",
    };

    private sealed record TmdbSearchResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbResult> Results { get; init; } = [];
    }

    private sealed record TmdbResult
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; init; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; init; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; init; }

        [JsonPropertyName("genre_ids")]
        public List<int>? GenreIds { get; init; }
    }

    private sealed record TmdbGenreListResponse
    {
        [JsonPropertyName("genres")]
        public List<TmdbGenre> Genres { get; init; } = [];
    }

    private sealed record TmdbGenre
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
    }
}
