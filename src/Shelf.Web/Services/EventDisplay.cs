using System.Text.Json;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Services;

// Prefix/Suffix wrap the item title (rendered separately by the view, e.g.
// as a link) rather than embedding it in one string, so callers never need
// to HTML-encode a title themselves.
public record EventDisplayModel(string Prefix, string Suffix, string? Detail, int? Stars);

public static class EventDisplay
{
    public static EventDisplayModel Describe(Event e) => e.Type switch
    {
        EventType.ItemAdded => new EventDisplayModel("added ", " to Shelf", null, null),
        EventType.ItemRemoved => new EventDisplayModel("removed ", " from Shelf", null, null),
        EventType.StatusChanged => new EventDisplayModel("changed status of ", BuildStatusChangeSuffix(e), null, null),
        EventType.ReviewAdded => DescribeReview("reviewed ", e),
        EventType.ReviewEdited => DescribeReview("edited the review of ", e),
        EventType.ReviewDeleted => DescribeReview("deleted the review of ", e),
        EventType.CommentAdded => DescribeComment("commented on ", e),
        EventType.CommentEdited => DescribeComment("edited a comment on ", e),
        EventType.CommentDeleted => DescribeComment("deleted a comment on ", e),
        EventType.RecommendationNoteChanged => DescribeNote("noted a recommendation for ", e),
        EventType.RecommendationNoteRemoved => DescribeNote("removed the recommendation note from ", e),
        _ => new EventDisplayModel(e.Type.ToString(), "", null, null),
    };

    private static string BuildStatusChangeSuffix(Event e)
    {
        var to = e.ToStatus.HasValue ? Display.StatusDisplay(e.ToStatus.Value) : "?";
        return e.FromStatus.HasValue ? $": {Display.StatusDisplay(e.FromStatus.Value)} → {to}" : $": {to}";
    }

    private static EventDisplayModel DescribeReview(string prefix, Event e)
    {
        var payload = TryDeserialize<ReviewPayload>(e.Payload);
        return new EventDisplayModel(prefix, "", payload?.Text, payload?.Rating);
    }

    private static EventDisplayModel DescribeComment(string prefix, Event e)
    {
        var payload = TryDeserialize<CommentPayload>(e.Payload);
        return new EventDisplayModel(prefix, "", payload?.Text, null);
    }

    private static EventDisplayModel DescribeNote(string prefix, Event e)
    {
        var payload = TryDeserialize<RecommendationNotePayload>(e.Payload);
        return new EventDisplayModel(prefix, "", payload?.Note, null);
    }

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, EventJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
