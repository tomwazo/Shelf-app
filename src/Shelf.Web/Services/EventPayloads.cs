using System.Text.Json;

namespace Shelf.Web.Services;

public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public record ReviewPayload(int? Rating, string? Text);

// No CommentId: embedding a comment's own not-yet-generated identity in its
// creation event would need a second SaveChangesAsync, breaking the
// single-transaction guarantee (see TECHNICAL_DESIGN.md § Transactional
// writes). The rendered text is enough for a human reading the Timeline.
public record CommentPayload(string Text);

public record RecommendationNotePayload(string Note);
