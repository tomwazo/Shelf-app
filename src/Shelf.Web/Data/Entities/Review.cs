namespace Shelf.Web.Data.Entities;

// One per item (v1). Hard-deleted when removed; history lives in Event
// snapshots. CreatedAt is the review date shown in the UI.
public class Review
{
    public int Id { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    // 1–5; enforced by check constraint alongside "rating or text non-null".
    public int? Rating { get; set; }

    public string? Text { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
