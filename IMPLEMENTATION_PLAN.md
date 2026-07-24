# Shelf v1 — Implementation Plan

Written for a coding agent implementing v1 in this repo. `project.md`
(product) and `TECHNICAL_DESIGN.md` (architecture) are canonical; this doc
operationalizes them into ordered phases. **The DB schema is complete — no
new EF migrations are needed for v1.** Flag it loudly if you believe
otherwise.

## Conventions

- New code layout under `src/Shelf.Web/`: `Identity/` (Easy Auth parsing,
  current user), `Services/` (mutations, stats, event payloads/display),
  `Search/` (providers + DTOs), pages flat under `Pages/`.
- Tests in `tests/Shelf.Web.Tests/` (xUnit), added to `Shelf.slnx`.
- Nullable stays enabled; no `!` suppressions except EF nav properties
  (follow existing entity style).
- Time: always `DateTimeOffset.UtcNow`. No TimeProvider abstraction —
  tests needing controlled times insert `Event` rows directly with chosen
  `OccurredAt`.
- **Transactions: every mutation = current-state change + Event append in
  one DbContext with a single `SaveChangesAsync()`** (EF wraps it in one
  implicit transaction). No explicit `BeginTransaction`, no unit-of-work
  ceremony, never a mid-method `SaveChanges`.
- Anti-forgery: Razor Pages defaults (form tag helpers). Never disable.
- JS: none required; progressive enhancement only. The one permitted
  inline JS is `onsubmit="return confirm(...)"` on destructive forms. No
  client framework, no fetch.
- Styling: Bootstrap 5.3.3 (already in wwwroot) + small additions to
  `wwwroot/css/site.css` (star display, cover placeholder). Must work at
  phone width (~390px).
- Enums bind by name in query strings/forms (`?type=Film`). Display names
  via `Services/Display.cs` helpers: `MediaTypeDisplay` ("TV", "Film"…),
  `MediaTypePlural` ("TV shows", "films"…), `StatusDisplay`
  ("In Progress"…).
- PRG: every `OnPost*` ends in `RedirectToPage`.
- HttpClient: typed clients via `AddHttpClient<T>`, 10 s timeout; provider
  failure renders a friendly inline message, never a 500.

## Configuration & secrets

| Purpose | Config key | Local (user secrets) | Prod (App Service setting) |
|---|---|---|---|
| TMDB v3 API key | `Tmdb:ApiKey` | `dotnet user-secrets set "Tmdb:ApiKey" "<key>"` | `Tmdb__ApiKey` |
| IGDB/Twitch client id | `Igdb:ClientId` | `dotnet user-secrets set "Igdb:ClientId" "<id>"` | `Igdb__ClientId` |
| IGDB/Twitch client secret | `Igdb:ClientSecret` | `dotnet user-secrets set "Igdb:ClientSecret" "<secret>"` | `Igdb__ClientSecret` |

OpenLibrary needs no key. Add `<UserSecretsId>shelf-web</UserSecretsId>`
to the csproj (Phase 2). Tom supplies the secret values (he has them).
Each provider throws a clear
`InvalidOperationException("Missing configuration 'Tmdb:ApiKey'")` at
first use if its key is absent.

Housekeeping: `git mv dotnet-tools.json .config/dotnet-tools.json` so
`dotnet tool restore` auto-discovers it.

## Page/route inventory

Delete `Pages/Index.cshtml(.cs)` and `Pages/Privacy.cshtml(.cs)`; keep
`Error`. `_Layout.cshtml`: brand **Shelf** → `/`; nav **Timeline** (`/`),
**Shelf** (`/shelf`), **Stats** (`/stats`); right-aligned **+ Add** →
`/add`; drop the Privacy footer link.

| Page | Route | Shows | Handlers |
|---|---|---|---|
| `Pages/Timeline.cshtml` | `@page "/"` | Global timeline, newest first. Period pills 24h / 7d (default) / 30d / All; event-type filter (All, Added, Removed, Status change, Review, Comment, Recommendation). Entry: cover thumb, description line (see Event display), "— {user}", time; whole entry links to `/items/{id}`. Cap 200 entries, no paging. Empty state "No items to display." | `OnGetAsync(string period = "7d", string? type = null)` |
| `Pages/Shelf.cshtml` | `@page "/shelf"` | Card grid of non-removed items: cover, title, media-type badge, status badge (nothing more per spec). Filter bar: media type, genre, status, consumed quick filter; sort select; filter-aware count line. Empty state as above. | `OnGetAsync(MediaType? type, int? genreId, ItemStatus? status, string? consumed, string sort = "added")` |
| `Pages/Add.cshtml` | `@page "/add"` | Step 1: media-type picker (5 radio pills) + query box, **GET form** (linkable/back-safe). Step 2 (same page when `mediaType`+`q` set): results — cover, title, year, genres; "Add" POST button with hidden fields; already-on-shelf results dimmed, unselectable, "Already on your Shelf" linking to item. No results: "No results found for '{q}'." | `OnGetAsync(MediaType? mediaType, string? q)`, `OnPostAddAsync(...)` → redirect `/items/{id}` |
| `Pages/Items/Details.cshtml` | `@page "/items/{id:int}"` | Structure below. | `OnGetAsync(int id)` plus POST handlers: `ChangeStatus`, `Remove`, `SaveReview`, `DeleteReview`, `AddComment`, `EditComment(commentId)`, `DeleteComment(commentId)`, `SaveRecommendation`, `RemoveRecommendation` |
| `Pages/Stats.cshtml` | `@page "/stats"` | Period pills Week (default) / Month / Year / All time; total; breakdown table by media type with In Progress / Finished columns; item list (cover, title, furthest-status badge, links to detail). Empty state as above. | `OnGetAsync(string period = "week")` |

**Item detail structure** (top→bottom):
1. *Removed banner* (if `RemovedAt != null`): "This item was removed from
   your Shelf on {date}. Re-add it from the Add page." All mutation UI
   hidden — the page becomes a read-only history view (re-add only via
   search, per spec).
2. *Header*: cover (CSS placeholder block — media-type-tinted, first
   letter of title — when URL is empty), title, media-type badge, genre
   badges, status badge, "Added {date}".
3. *Status control*: inline POST form, `<select>` of three statuses +
   Update. Any transition allowed; posting the current status is a no-op
   (no event).
4. *Recommendation note*: current note + Edit/Remove, or "Add
   recommendation note" form.
5. *Review*: stars + text + "Reviewed {CreatedAt:d}" (+ "edited
   {UpdatedAt:d}" if different) + Edit/Delete; or add form — 5 star radios
   (pure CSS) + textarea, at least one required (validate server-side
   before the check constraint can throw).
6. *Comments*: list (text, timestamp, edited marker) each with
   Edit/Delete; add-comment textarea below.
7. *Remove button*: `btn-outline-danger`, confirm() guard.
8. *Item timeline*: all this item's events, oldest→newest, same rendering
   helper as global Timeline (no cover thumbs).

Inline-edit convention (no JS): Edit links are GET links back to the same
page with a query flag (`?editReview=1`, `?editComment={id}`,
`?editRec=1`); OnGet renders that section as a prefilled form.

## Service layer

Queries live in page models (each is a single LINQ query used in one
place — a query service would be indirection without payoff). Extracted
into services: **all mutations** (`ShelfService` — the state+event pairing
implemented exactly once) and **the Stats query** (`StatsService` —
subtlest logic, needs unit tests). Event→display mapping is a static
helper shared by two pages.

### `Identity/CurrentUserService.cs`

```csharp
public interface ICurrentUserService { Task<User> GetCurrentUserAsync(CancellationToken ct = default); }
public class CurrentUserService(IHttpContextAccessor http, ShelfDbContext db, IHostEnvironment env) : ICurrentUserService
{ /* caches resolved User in a field for the request (scoped) */ }
```

1. Read `X-MS-CLIENT-PRINCIPAL` header → base64 → JSON
   `{ "auth_typ": …, "claims": [ { "typ": …, "val": … } ] }`.
2. ExternalIdentity claim, first match:
   `http://schemas.microsoft.com/identity/claims/objectidentifier`
   (stable Entra oid), else `sub`, else `…/nameidentifier`. **Never**
   name/email (mutable).
3. Name claim, first of: `name`,
   `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name`,
   `preferred_username`; fallback `"User"`.
4. Header absent: Development → fixed `ExternalIdentity = "local-dev"`,
   Name "Tom" (per TECHNICAL_DESIGN.md § Local Development). Production →
   **throw** (behind Easy Auth the header must exist; fail loud, never
   mis-attribute).
5. Look up by `ExternalIdentity`, auto-create on first sight. Don't
   update Name on later logins.

Register `AddHttpContextAccessor()` + scoped service. No middleware, no
ASP.NET authentication — Easy Auth is the wall; the existing
`UseAuthorization()` line stays as-is.

### `Services/ShelfService.cs` — all mutations

```csharp
public class ShelfService(ShelfDbContext db, ICurrentUserService currentUser)
{
    Task<Item> AddOrRestoreItemAsync(MediaType mediaType, ExternalSource source, string externalId,
        string title, string coverArtUrl, IReadOnlyList<string> genreNames);
    Task ChangeStatusAsync(int itemId, ItemStatus newStatus);
    Task RemoveItemAsync(int itemId);
    Task SaveReviewAsync(int itemId, int? rating, string? text);   // insert or update
    Task DeleteReviewAsync(int itemId);
    Task<Comment> AddCommentAsync(int itemId, string text);
    Task EditCommentAsync(int itemId, int commentId, string text);
    Task DeleteCommentAsync(int itemId, int commentId);
    Task SaveRecommendationNoteAsync(int itemId, string note);     // add or edit
    Task RemoveRecommendationNoteAsync(int itemId);
}
```

Shared pattern: load → mutate current state → append
`Event { ItemId, UserId, OccurredAt = UtcNow, Type, FromStatus/ToStatus or Payload }`
→ one `SaveChangesAsync()`. Specifics:

- **AddOrRestoreItemAsync**: look up by `(source, externalId)` — any row,
  removed or not. Active row → return unchanged, no event (idempotent).
  Removed row → **restore**: clear `RemovedAt`, keep prior
  status/review/comments/note/genres/metadata (don't overwrite from
  posted data), log `ItemAdded`. Never insert for an existing key (unique
  index rejects it). No row → insert `{ Status = Backlog, AddedAt = UtcNow }`,
  attach genres (by trimmed name; find-or-create — SQL Server CI
  collation handles casing), log `ItemAdded`.
- **ChangeStatusAsync**: same status → return, no event. Else set + log
  `StatusChanged` with promoted `FromStatus`/`ToStatus`, `Payload = null`.
  Any transition allowed.
- **RemoveItemAsync**: `RemovedAt = UtcNow`, log `ItemRemoved`.
- **SaveReviewAsync**: validate server-side (rating null-or-1–5; at least
  one of rating/text non-empty). No review → insert
  (`CreatedAt = UpdatedAt = UtcNow`), log `ReviewAdded` snapshot. Exists →
  update + `UpdatedAt`, log `ReviewEdited` with **new** values.
- **DeleteReviewAsync**: capture `{rating, text}`, **hard-delete**, log
  `ReviewDeleted` with snapshot. Later SaveReview = brand-new row, new
  `CreatedAt` (that IS the review date — by design).
- **Comments**: analogous; deletes are hard-deletes with last-known-text
  snapshot; Edit/Delete verify the comment belongs to `itemId`.
- **Recommendation note**: set/overwrite `Item.RecommendationNote`, log
  `RecommendationNoteChanged` with new value; remove → capture last
  value, null it, log `RecommendationNoteRemoved` with it.
- **Guard**: every mutation except AddOrRestore throws if
  `RemovedAt != null` (removed items are read-only; UI hides forms, this
  is the backstop).

### Cover art convention

`CoverArtUrl` is a required (non-null) column; providers without a cover
pass `""`. Rendering rule everywhere: empty/whitespace → CSS placeholder
block. Broken hotlinks just show the browser's broken-image state
(accepted trade-off per TECHNICAL_DESIGN.md). No migration needed.

### `Services/StatsService.cs`

```csharp
public record StatsRow(MediaType MediaType, int InProgress, int Finished);
public record StatsItem(int ItemId, string Title, string CoverArtUrl, MediaType MediaType,
    ItemStatus FurthestStatus, DateTimeOffset LatestActivityAt);
public record StatsResult(int Total, IReadOnlyList<StatsRow> ByMediaType, IReadOnlyList<StatsItem> Items);
public class StatsService(ShelfDbContext db) { Task<StatsResult> GetStatsAsync(DateTimeOffset? since); } // null = all time
```

Query: `Events` where `Type == StatusChanged && ToStatus ∈ {InProgress,
Finished} && (since == null || OccurredAt >= since)`, joined to Item.
Dedupe **in memory** (fine at this scale): group by ItemId;
`FurthestStatus = any ToStatus == Finished ? Finished : InProgress` —
Finished beats InProgress **regardless of event order** (Finished → back
to InProgress within the window still counts Finished).
`LatestActivityAt` = max OccurredAt (sort item list by it desc).
**Removed items included** — the activity happened, and their detail
pages render. Page maps presets: week `-7d`, month `-30d`, year `-365d`,
all `null`.

### `Services/EventPayloads.cs` + `Services/EventDisplay.cs`

Payload records serialized with shared
`JsonSerializerOptions(JsonSerializerDefaults.Web)` (camelCase). **Exact
payload contract**:

| EventType | Promoted cols | Payload JSON |
|---|---|---|
| ItemAdded / ItemRemoved | — | `null` (title always available from the Item row — soft delete) |
| StatusChanged | FromStatus, ToStatus | `null` |
| ReviewAdded / ReviewEdited | — | `{"rating": 4, "text": "…"}` (either nullable) — values as of that action |
| ReviewDeleted | — | `{"rating": 4, "text": "…"}` — last-known |
| CommentAdded / CommentEdited | — | `{"commentId": 12, "text": "…"}` |
| CommentDeleted | — | `{"commentId": 12, "text": "…"}` — last-known |
| RecommendationNoteChanged | — | `{"note": "…"}` — new value |
| RecommendationNoteRemoved | — | `{"note": "…"}` — last-known |

`EventDisplay.Describe(Event e)` → `{ ActionText, Detail?, Stars? }`,
deserializing tolerantly (malformed → omit detail, never throw). Strings:
"added **{title}** to Shelf", "removed **{title}** from Shelf", "changed
status of **{title}**: {From} → {To}", "reviewed / edited review of /
deleted review of **{title}**" (detail = ★ + text), "commented on /
edited a comment on / deleted a comment on **{title}**" (detail = text),
"noted a recommendation for / removed the recommendation note from
**{title}**" (detail = note). Views append "— {e.User.Name}".

## External search (`Search/`)

```csharp
public record MediaSearchResult(ExternalSource Source, string ExternalId, string Title,
    string? Year, string CoverArtUrl, IReadOnlyList<string> Genres); // "" cover = none

public interface IMediaSearchProvider
{
    bool Supports(MediaType mediaType);
    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(MediaType mediaType, string query, CancellationToken ct);
}
public class MediaSearchService(IEnumerable<IMediaSearchProvider> providers) { /* single Supports() match; limit 20 */ }
```

Registration: `AddHttpClient<TmdbSearchProvider>()` etc., each also as
`IMediaSearchProvider`; `IgdbTokenProvider` is a **singleton** using
`IHttpClientFactory`; `AddMemoryCache()`.

**Media type → provider**: Tv → TMDB `/search/tv`; Film → TMDB
`/search/movie`; **Documentary → TMDB both, merged (movies then TV), each
labeled "(Film)" / "(TV series)" beside the year, all stamped
`MediaType.Documentary`** (TMDB treats documentary as a genre, not a
type; the user's pre-picked media type stamps the item — simple and
honest); Book → OpenLibrary; Game → IGDB.

**TMDB ExternalId format — critical**: movie and TV id spaces overlap,
and the unique key is `(ExternalSource, ExternalId)`, so store
**`"movie:{id}"` / `"tv:{id}"`**. This also makes the duplicate check
correct when the same record is reachable via Film and Documentary
searches (same film added as Film then found via Documentary → "Already
on your Shelf" — correct).

- **TMDB**: `api_key` query param from `Tmdb:ApiKey`.
  `GET https://api.themoviedb.org/3/search/movie?api_key={k}&query={q}`
  (fields `id, title, release_date, poster_path, genre_ids`);
  `/3/search/tv` (`name, first_air_date`). Genre ids → names via
  `/3/genre/movie/list` + `/3/genre/tv/list`, cached in `IMemoryCache`
  24 h. Cover: `https://image.tmdb.org/t/p/w342{poster_path}` (hardcode
  base; skip `/configuration`); null → `""`. Year = first 4 chars of the
  date.
- **OpenLibrary** (no auth; send
  `User-Agent: Shelf/1.0 (tom87moore@gmail.com)`):
  `GET https://openlibrary.org/search.json?q={q}&limit=20&fields=key,title,first_publish_year,cover_i,subject`.
  ExternalId: strip `/works/` prefix from `key`. Cover:
  `https://covers.openlibrary.org/b/id/{cover_i}-M.jpg` or `""`. Genres:
  `subject` is noisy — first 5 entries, skip any > 40 chars; may be
  empty.
- **IGDB**: `IgdbTokenProvider` (singleton):
  `GetTokenAsync(bool forceRefresh = false)` →
  `POST https://id.twitch.tv/oauth2/token?client_id=…&client_secret=…&grant_type=client_credentials`
  → `{access_token, expires_in}` (~60 days). Cache token + expiry in a
  field behind `SemaphoreSlim(1,1)`; refresh when missing/forced/within
  24 h of expiry. **Never fetch per request.** Search:
  `POST https://api.igdb.com/v4/games`, headers `Client-ID`,
  `Authorization: Bearer`; plain-text body
  `search "{q}"; fields name, first_release_date, cover.image_id, genres.name; limit 20;`
  (escape `"`/`\` in q). On 401: force-refresh, retry once. Cover:
  `https://images.igdb.com/igdb/image/upload/t_cover_big/{image_id}.jpg`.
  Year from Unix `first_release_date`. ExternalId: numeric id as string.

**Add flow**: GET results → page model loads non-removed items matching
any returned `(Source, ExternalId)` in one query → flags "Already on your
Shelf" (removed items deliberately not flagged — selectable per spec).
Select → POST hidden fields
(`MediaType, Source, ExternalId, Title, CoverArtUrl, Genres[]`) →
`AddOrRestoreItemAsync` → redirect `/items/{id}`. Trusting posted
metadata is fine (single-user behind Easy Auth + anti-forgery) and avoids
per-provider detail-fetch endpoints.

## Feature behavior reference (traps baked in)

- Duplicate check: non-removed rows only. Removed rows: restore, never
  insert.
- Shelf query: always `RemovedAt == null`. Genre filter
  `Genres.Any(g => g.Id == genreId)`; genre dropdown lists only genres
  with ≥ 1 non-removed item. **Consumed = InProgress ∪ Finished; Not
  consumed = Backlog.** Sorts: `added` (AddedAt desc, default), `az`,
  `za`. Count line reflects filters: "12 films" / "3 TV shows" with a
  media-type filter, else "{n} items"; handle singular.
- Timeline: events for removed items included (no RemovedAt filter — Item
  joined only for title/cover). Order `OccurredAt` desc, then `Id` desc
  (stable same-instant tiebreak). Type filter groups: Review =
  Added|Edited|Deleted etc. per table above.
- Item detail renders for removed items (read-only + banner).
- Review delete → re-add = new row, new CreatedAt.
- Stats: per item, furthest status in window, Finished > InProgress;
  removed items included.
- `Include` `Event.User`/`Event.Item` in Timeline/detail queries — no
  lazy loading is configured.

## Testing

- `tests/Shelf.Web.Tests/` — xUnit; packages `xunit`,
  `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
  `Microsoft.EntityFrameworkCore.Sqlite`; reference `Shelf.Web`; add to
  `Shelf.slnx`.
- **Test DB: SQLite in-memory.** EF InMemory enforces neither unique
  indexes nor check constraints (useless for re-add/review tests);
  LocalDB is faithful but slow with lifecycle hassle. SQLite enforces
  both, and the existing check-constraint SQL's `[bracketed]` identifiers
  parse fine in SQLite. **Known divergence handled**: SQLite can't
  compare/order `DateTimeOffset` in SQL →
  `TestShelfDbContext : ShelfDbContext` overrides `OnModelCreating` (call
  base, then apply `DateTimeOffsetToBinaryConverter` to every
  DateTimeOffset property). Safe because the app writes UTC only.
- Fixture: per-test `SqliteConnection("DataSource=:memory:")` +
  `EnsureCreated()`; dispose closes (destroys DB). Helper seeds a User +
  stub `ICurrentUserService`.
- Suites:
  - **CurrentUserServiceTests**: `GetCurrentUser_FirstRequest_AutoCreatesUserRow`,
    `GetCurrentUser_ExistingExternalIdentity_ReturnsSameRow`,
    `GetCurrentUser_EasyAuthHeader_UsesOidClaimForExternalIdentity`,
    `GetCurrentUser_NoHeader_InDevelopment_UsesLocalDevIdentity`,
    `GetCurrentUser_NoHeader_InProduction_Throws`.
  - **ShelfServiceTests**: `AddItem_New_CreatesBacklogItem_AndItemAddedEvent`,
    `AddItem_CreatesGenres_AndReusesExistingByName`,
    `AddItem_ActiveDuplicate_ReturnsExistingWithoutNewEvent`,
    `AddItem_RemovedItem_RestoresRow_KeepsStatusAndReview_LogsItemAdded`,
    `RemoveItem_SetsRemovedAt_AndLogsItemRemoved`,
    `ChangeStatus_LogsEventWithFromAndToColumns`,
    `ChangeStatus_SameStatus_NoEvent`,
    `SaveReview_New_InsertsAndLogsReviewAddedSnapshot`,
    `SaveReview_Existing_UpdatesAndLogsReviewEditedSnapshot`,
    `DeleteReview_HardDeletes_AndLogsLastKnownSnapshot`,
    `SaveReview_AfterDelete_NewRowWithNewCreatedAt`,
    `SaveReview_EmptyRatingAndText_Throws`,
    `DeleteComment_LogsLastKnownTextWithCommentId`,
    `Mutation_OnRemovedItem_Throws`.
  - **StatsServiceTests**: `Stats_FinishedInWindow_CountsAsFinished`,
    `Stats_InProgressThenFinishedInWindow_CountsOnce_AsFinished`,
    `Stats_FinishedThenBackToInProgress_StillCountsAsFinished`,
    `Stats_ChangeBeforeWindow_Excluded`,
    `Stats_BreaksDownByMediaType`, `Stats_IncludesRemovedItems`,
    `Stats_AllTime_IncludesEverything`.

## Phases

Each phase ends with `dotnet build` clean, `dotnet test` green, app
runnable via `dotnet watch --project src\Shelf.Web`. Commit per phase.

### Phase 0 — Housekeeping + test scaffold
Move tool manifest to `.config/`; create test project (placeholder fact)
in `Shelf.slnx`. Verify: `dotnet tool restore` finds manifest;
build/test/run all fine.

### Phase 1 — Identity + layout + page shells
`CurrentUserService` + registrations; rewrite `_Layout` nav; delete
Index/Privacy; create Timeline (`@page "/"`), Shelf, Stats stubs with
empty states; `Display.cs`. Tests: identity suite. Verify: three nav
pages render "No items to display"; LocalDB `Users` gains one row
(`local-dev`/"Tom") after first request.

### Phase 2 — Search & add
All of `Search/`; memory cache + HttpClient registrations;
`UserSecretsId` + set 3 secrets (Tom provides values);
`ShelfService.AddOrRestoreItemAsync` (full logic incl. restore branch);
`EventPayloads.cs`; `Pages/Add.cshtml`. Tests: `AddItem_*` (seed a
removed item directly for the restore test). Verify manually: add a
film, TV show, documentary (both movie+TV results appear; DB ExternalIds
are `movie:`/`tv:`-prefixed), book (incl. one with no cover →
placeholder), game (add two games — one token fetch in logs). Re-search
the film → "Already on your Shelf". DB: each add = 1 Item + 1 ItemAdded
event; genres shared across items.

### Phase 3 — Shelf page
Full browse view: card grid, filters, consumed quick filter, sorts,
filter-aware counts, genre dropdown; placeholder CSS. Verify: counts
track filters; filters combine and survive in query string; empty combo
→ empty state; phone width OK.

### Phase 4 — Item detail: status, remove, re-add, per-item timeline
`Pages/Items/Details` header + status control + remove + removed
banner/read-only mode + item timeline via `EventDisplay.cs` (full
display table now — later event types just start appearing). Link
cards/entries here. Tests: `ChangeStatus_*`, `RemoveItem_*`,
`Mutation_OnRemovedItem_Throws`. Verify: Backlog→Finished direct; remove
→ gone from Shelf, detail read-only; re-add via search → prior status +
history + new added event, same `Item.Id`.

### Phase 5 — Review, comments, recommendation note
Remaining `ShelfService` methods; detail page sections with inline-edit
query flags; star CSS. Tests: review/comment suites. Verify: full review
lifecycle (delete → re-add gets new date); comments CRUD; note
add/edit/remove; item timeline shows correct snapshots (deleted
comment's text still readable in history).

### Phase 6 — Global Timeline
Replace stub: period pills (default 7d), type filter, EventDisplay
rendering, attribution, 200 cap. Verify: filters combine; removed items'
events appear and click through.

### Phase 7 — Stats
`StatsService` + page. Tests: stats suite. Verify with crafted data:
item Backlog→InProgress→Finished in-window counts once as Finished; item
In Progress only; item finished last month appears in Month/Year not
Week; breakdown + item list match.

### Phase 8 — Polish, deploy, prod config, smoke test
Sweep pages at 390 px; page titles; empty states. Merge `develop` →
`main` via PR → Actions deploys. Set prod settings:
`az webapp config appsettings set -g shelf-rg -n shelf-app-ccbp --settings Tmdb__ApiKey=<v> Igdb__ClientId=<v> Igdb__ClientSecret=<v>`.
No DB work (schema already applied). Prod smoke test: sign in →
Timeline; **prod `Users` row has the Entra oid as ExternalIdentity (not
"local-dev", not an email)**; add one item per provider (proves all keys
+ IGDB token flow); status/review/comment/note on one item;
remove/re-add; Shelf filters; Stats; on a phone browser. Azure SQL
auto-pause cold start on first request is expected, not a failure.

## Implementer notes

- Keep provider JSON DTOs as `private` records inside each provider
  file — API shape changes stay contained.
- The single-`SaveChangesAsync` rule in `ShelfService` IS the
  transactionality guarantee — never split it.
- If anything seems to require a schema change, stop and flag it — v1 is
  designed to need none.
