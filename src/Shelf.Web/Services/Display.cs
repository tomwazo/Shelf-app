using Shelf.Web.Data.Entities;

namespace Shelf.Web.Services;

public static class Display
{
    public static string MediaTypeDisplay(MediaType mediaType) => mediaType switch
    {
        MediaType.Tv => "TV",
        MediaType.Film => "Film",
        MediaType.Documentary => "Documentary",
        MediaType.Book => "Book",
        MediaType.Game => "Game",
        _ => mediaType.ToString(),
    };

    public static string MediaTypePlural(MediaType mediaType) => mediaType switch
    {
        MediaType.Tv => "TV shows",
        MediaType.Film => "films",
        MediaType.Documentary => "documentaries",
        MediaType.Book => "books",
        MediaType.Game => "games",
        _ => mediaType.ToString(),
    };

    public static string MediaTypeSingular(MediaType mediaType) => mediaType switch
    {
        MediaType.Tv => "TV show",
        MediaType.Film => "film",
        MediaType.Documentary => "documentary",
        MediaType.Book => "book",
        MediaType.Game => "game",
        _ => mediaType.ToString(),
    };

    public static string StatusDisplay(ItemStatus status) => status switch
    {
        ItemStatus.Backlog => "Backlog",
        ItemStatus.InProgress => "In Progress",
        ItemStatus.Finished => "Finished",
        _ => status.ToString(),
    };
}
