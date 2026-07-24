using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Shelf.Web.Search.Providers;

// Singleton: caches the Twitch client-credentials token across requests
// (tokens are valid ~60 days) instead of fetching one per search.
public class IgdbTokenProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private readonly HttpClient _http = httpClientFactory.CreateClient(nameof(IgdbTokenProvider));
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string> GetTokenAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (IsTokenUsable(forceRefresh))
        {
            return _accessToken!;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (IsTokenUsable(forceRefresh))
            {
                return _accessToken!;
            }

            var clientId = configuration["Igdb:ClientId"]
                ?? throw new InvalidOperationException("Missing configuration 'Igdb:ClientId'");
            var clientSecret = configuration["Igdb:ClientSecret"]
                ?? throw new InvalidOperationException("Missing configuration 'Igdb:ClientSecret'");

            var url = $"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials";

            var response = await _http.PostAsync(url, content: null, ct);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Twitch token response was empty.");

            _accessToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Refresh a day before expiry rather than cutting it exactly at the wire.
    private bool IsTokenUsable(bool forceRefresh) =>
        !forceRefresh && _accessToken is not null && DateTimeOffset.UtcNow < _expiresAt - TimeSpan.FromHours(24);

    private sealed record TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
