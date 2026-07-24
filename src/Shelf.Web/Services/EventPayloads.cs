using System.Text.Json;

namespace Shelf.Web.Services;

public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public record ReviewPayload(int? Rating, string? Text);

public record CommentPayload(int CommentId, string Text);

public record RecommendationNotePayload(string Note);
