namespace Shelf.Web.Data.Entities;

// Append-only log driving the Timeline (global and per-item) and Stats.
// Rows are never updated or deleted.
public class Event
{
    public int Id { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    public EventType Type { get; set; }

    // Promoted columns, populated only for StatusChanged events.
    // ToStatus drives Stats; FromStatus is display-only.
    public ItemStatus? FromStatus { get; set; }

    public ItemStatus? ToStatus { get; set; }

    // JSON snapshot for all other event types; shape depends on Type
    // (e.g. ReviewEdited → {rating, text}).
    public string? Payload { get; set; }
}
