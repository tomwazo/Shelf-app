using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Search.Providers;

public class IgdbSearchProvider(HttpClient http, IgdbTokenProvider tokenProvider, IConfiguration configuration) : IMediaSearchProvider
{
    public bool Supports(MediaType mediaType) => mediaType == MediaType.Game;

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct = default)
    {
        var clientId = configuration["Igdb:ClientId"]
            ?? throw new InvalidOperationException("Missing configuration 'Igdb:ClientId'");

        var token = await tokenProvider.GetTokenAsync(ct: ct);
        var response = await SendSearchAsync(clientId, token, query, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            token = await tokenProvider.GetTokenAsync(forceRefresh: true, ct: ct);
            response = await SendSearchAsync(clientId, token, query, ct);
        }

        response.EnsureSuccessStatusCode();

        var games = await response.Content.ReadFromJsonAsync<List<IgdbGame>>(cancellationToken: ct) ?? [];
        return games.Select(Map).ToList();
    }

    private async Task<HttpResponseMessage> SendSearchAsync(string clientId, string token, string query, CancellationToken ct)
    {
        var escapedQuery = query.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var body = $"""search "{escapedQuery}"; fields name, first_release_date, cover.image_id, genres.name; limit 20;""";

        using var request = new HttpRequestMessage(HttpMethod.Post, "games")
        {
            Content = new StringContent(body),
        };
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await http.SendAsync(request, ct);
    }

    private static MediaSearchResult Map(IgdbGame game)
    {
        var year = game.FirstReleaseDate.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(game.FirstReleaseDate.Value).UtcDateTime.Year.ToString()
            : null;

        var cover = game.Cover?.ImageId is { Length: > 0 } imageId
            ? $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg"
            : "";

        var genres = (game.Genres ?? [])
            .Select(g => g.Name)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();

        return new MediaSearchResult(ExternalSource.Igdb, game.Id.ToString(), game.Name ?? "(untitled)", year, cover, genres);
    }

    private sealed record IgdbGame
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("first_release_date")]
        public long? FirstReleaseDate { get; init; }

        [JsonPropertyName("cover")]
        public IgdbCover? Cover { get; init; }

        [JsonPropertyName("genres")]
        public List<IgdbGenre>? Genres { get; init; }
    }

    private sealed record IgdbCover
    {
        [JsonPropertyName("image_id")]
        public string? ImageId { get; init; }
    }

    private sealed record IgdbGenre
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }
}
