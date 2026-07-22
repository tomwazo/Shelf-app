namespace Shelf.Web.Data.Entities;

// Hard-deleted when removed; history lives in Event snapshots.
public class Comment
{
    public int Id { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public required string Text { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
