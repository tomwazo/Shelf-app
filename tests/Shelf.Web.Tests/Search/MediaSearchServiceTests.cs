using Shelf.Web.Data.Entities;
using Shelf.Web.Search;

namespace Shelf.Web.Tests.Search;

public class MediaSearchServiceTests
{
    [Fact]
    public async Task SearchAllAsync_MergesResultsFromMultipleProviders_KeyedByTheirOwnTypes()
    {
        var books = new FakeMediaSearchProvider(MediaType.Book, [Result(ExternalSource.OpenLibrary, "b1", "Book One")]);
        var games = new FakeMediaSearchProvider(MediaType.Game, [Result(ExternalSource.Igdb, "g1", "Game One")]);
        var service = new MediaSearchService([books, games]);

        var byType = await service.SearchAllAsync("query");

        Assert.Equal("Book One", Assert.Single(byType[MediaType.Book].Top).Title);
        Assert.Equal("Game One", Assert.Single(byType[MediaType.Game].Top).Title);
    }

    [Fact]
    public async Task SearchAllAsync_TruncatesToTopFive_ButReportsTrueTotalCount()
    {
        var results = Enumerable.Range(1, 8).Select(i => Result(ExternalSource.Tmdb, $"m{i}", $"Movie {i}")).ToList();
        var provider = new FakeMediaSearchProvider(MediaType.Film, results);
        var service = new MediaSearchService([provider]);

        var byType = await service.SearchAllAsync("query");

        var film = byType[MediaType.Film];
        Assert.Equal(5, film.Top.Count);
        Assert.Equal(8, film.TotalCount);
        Assert.False(film.Failed);
    }

    [Fact]
    public async Task SearchAllAsync_OneProviderThrows_OtherProvidersResultsStillReturned()
    {
        var books = new FakeMediaSearchProvider(MediaType.Book, [Result(ExternalSource.OpenLibrary, "b1", "Book One")]);
        var brokenGames = new FakeMediaSearchProvider(MediaType.Game, throwOnSearch: true);
        var service = new MediaSearchService([books, brokenGames]);

        var byType = await service.SearchAllAsync("query");

        Assert.False(byType[MediaType.Book].Failed);
        Assert.Equal("Book One", Assert.Single(byType[MediaType.Book].Top).Title);

        Assert.True(byType[MediaType.Game].Failed);
        Assert.Empty(byType[MediaType.Game].Top);
        Assert.Equal(0, byType[MediaType.Game].TotalCount);
    }

    private static MediaSearchResult Result(ExternalSource source, string externalId, string title) =>
        new(source, externalId, title, Year: null, CoverArtUrl: "", Genres: []);

    private sealed class FakeMediaSearchProvider : IMediaSearchProvider
    {
        private readonly MediaType _type;
        private readonly IReadOnlyList<MediaSearchResult> _results;
        private readonly bool _throwOnSearch;

        public FakeMediaSearchProvider(MediaType type, IReadOnlyList<MediaSearchResult>? results = null, bool throwOnSearch = false)
        {
            _type = type;
            _results = results ?? [];
            _throwOnSearch = throwOnSearch;
        }

        public bool Supports(MediaType mediaType) => mediaType == _type;

        public IReadOnlyList<MediaType> SupportedTypes => [_type];

        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct = default) =>
            Task.FromResult(_results);

        public Task<IReadOnlyDictionary<MediaType, IReadOnlyList<MediaSearchResult>>> SearchAllAsync(string query, CancellationToken ct = default)
        {
            if (_throwOnSearch)
            {
                throw new HttpRequestException("simulated provider failure");
            }

            IReadOnlyDictionary<MediaType, IReadOnlyList<MediaSearchResult>> byType =
                new Dictionary<MediaType, IReadOnlyList<MediaSearchResult>> { [_type] = _results };
            return Task.FromResult(byType);
        }
    }
}
