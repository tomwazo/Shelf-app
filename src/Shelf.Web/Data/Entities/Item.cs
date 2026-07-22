namespace Shelf.Web.Data.Entities;

public class Item
{
    public int Id { get; set; }

    public MediaType MediaType { get; set; }

    public required string Title { get; set; }

    public required string CoverArtUrl { get; set; }

    public ExternalSource ExternalSource { get; set; }

    public required string ExternalId { get; set; }

    public ItemStatus Status { get; set; } = ItemStatus.Backlog;

    public string? RecommendationNote { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    // Soft delete: hidden from the Shelf when set, never hard-deleted.
    public DateTimeOffset? RemovedAt { get; set; }

    public List<Genre> Genres { get; set; } = [];

    public Review? Review { get; set; }

    public List<Comment> Comments { get; set; } = [];

    public List<Event> Events { get; set; } = [];
}
