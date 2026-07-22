namespace Shelf.Web.Data.Entities;

public enum MediaType
{
    Tv,
    Film,
    Documentary,
    Book,
    Game
}

public enum ItemStatus
{
    Backlog,
    InProgress,
    Finished
}

public enum ExternalSource
{
    Tmdb,
    OpenLibrary,
    Igdb
}

public enum EventType
{
    ItemAdded,
    ItemRemoved,
    StatusChanged,
    ReviewAdded,
    ReviewEdited,
    ReviewDeleted,
    CommentAdded,
    CommentEdited,
    CommentDeleted,
    RecommendationNoteChanged,
    RecommendationNoteRemoved
}
