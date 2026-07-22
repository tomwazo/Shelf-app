# Shelf — Technical Design

This is the canonical reference for technical/architecture decisions for
Shelf. Product scope and behavior live in [PROJECT.md](PROJECT.md); this
doc covers how it's built. Updated as decisions are made.

## Status

**Data model: settled. Stack & hosting: settled** (see below), no open
questions. Next up: project scaffolding and API/page design — not yet
started.

## Stack & Hosting

### Stack: C# / ASP.NET Core, Razor Pages, EF Core
- **Backend + frontend:** ASP.NET Core with **Razor Pages** —
  server-rendered pages with minimal JavaScript. Shelf is fundamentally
  a CRUD app (browse/filter lists, detail pages, forms), which is Razor
  Pages' sweet spot; it avoids the extra moving parts of a SPA or
  Blazor's persistent connections (a poor fit for phone browsers).
  Can be revisited if a page turns out to need heavy interactivity.
- **Data access:** EF Core.

### Database: Azure SQL Database (free tier)
- SQLite was the default instinct for a single-user app, but App
  Service's persistent storage is a network file share (Azure Files/
  SMB), where SQLite's file locking is unreliable — a known corruption
  risk. Azure SQL's free tier (10 GB, monthly vCore-seconds allowance)
  is genuinely free and the native pairing with EF Core.
- The data model translates unchanged; auto-increment integer PKs map
  to `IDENTITY`, the Event `payload` column is JSON stored as
  `nvarchar(max)`.

### Hosting & deployment: Azure App Service via GitHub Actions
- Single App Service (Linux) app; CI/CD from the GitHub repo via the
  Actions workflow App Service generates.
- **Access control:** App Service built-in authentication ("Easy
  Auth") puts a Microsoft-account login wall in front of the whole app
  with zero application code — Shelf shouldn't be publicly reachable.
  Who can get in is controlled in the identity provider config, not
  app code.

### User identity: Easy Auth principal → User row
Instead of the originally planned hardcoded placeholder user, the app
maps the Easy Auth identity to a real `User` row: middleware reads the
authenticated principal from the request headers, looks up the matching
`User` (auto-creating it on first login), and stamps that `user_id` on
events, reviews, and comments.
- **Why:** near-zero extra cost over the placeholder — the schema
  already carries `user_id` everywhere — and it means authentication is
  *done* if/when friends/family are invited. With only one authorized
  user it behaves identically to the single-user plan.
- **Scope guard:** data semantics stay single-user in v1. Multi-user
  product questions — shared shelf vs per-user shelves, per-user status,
  multiple reviews per item — remain deferred and undecided
  ([PROJECT.md](PROJECT.md) § Users). Inviting a second user before
  resolving those would produce odd behavior (e.g. a shared `Item.status`),
  so the identity provider config should stay locked to one user until
  then.

## Architecture Decisions

### Event log: current state + append-only history
Two kinds of storage, kept separate:
- **Current-state tables** (Item, Review, Comment, etc.) hold what's true
  right now — what the app reads/writes for normal CRUD operations.
- **Event log** (append-only) records what happened, for the Timeline and
  Stats features.

Events store an immutable **snapshot** of the relevant content at the time
of the action (e.g. a `review_edited` event stores the rating/text as they
were at that edit), rather than a live reference to the current row.
- **Why:** editing or deleting a comment/review shouldn't rewrite how past
  Timeline entries read. A snapshot gives a true history — e.g. "added:
  'good so far'" followed later by "edited: 'great, finished it'" — even
  after the current text has changed again or the comment is deleted.

**Consequence:** Review and Comment rows can be **hard-deleted** from
current state when removed — their content isn't needed there anymore
once removed, since the Event log already holds the snapshots that
preserve history. Item rows are the exception (see below).

### Item removal: soft delete
Item has a `removed_at` flag rather than being hard-deleted.
- **Why:** Timeline entries link to the item's detail page, and that page
  must still render (title, cover art, full history) after removal — the
  item is hidden from the Shelf, not erased. Review/Comment don't have
  this constraint since nothing links directly to a standalone comment/
  review page.

### Re-adding a removed item: un-remove the existing row
Because removal is a soft delete, a removed item's row still occupies the
unique `(external_source, external_id)` key — a fresh insert for the same
external item would be rejected. Instead, selecting a previously removed
item in search **restores the existing row**: `removed_at` is cleared and
a new `item_added` event is logged.
- The item returns with its prior status, review, comments, and
  recommendation note intact, and its full history stays on one detail
  page. This is a deliberate exception to "status always defaults to
  Backlog on add" (see [PROJECT.md](PROJECT.md) journey 1).
- **Consequence for search:** the "already on Shelf" duplicate check must
  only match non-removed rows — removed items appear as selectable
  results, not as "already added."

### Genre: normalized table
Genre is its own table with a many-to-many join to Item, rather than a
free-form tag list stored per item.
- **Why:** avoids duplicate/inconsistent spellings (e.g. "Sci-Fi" vs
  "Science Fiction") across items and keeps Shelf genre filtering clean.

### Event payload: hybrid (JSON + promoted columns)
The Event table has a JSON payload column by default, but `status_changed`
events get their `from`/`to` status promoted to real columns on the Event
table itself, rather than living in JSON.
- **Why:** most event types (review/comment/recommendation-note snapshots)
  are display-only — rendered on the Timeline as text, never queried by
  content — so JSON is simplest and needs no migration to add new event
  types. `status_changed` is the exception: the Stats feature filters and
  groups directly on the target status, so promoting it to a plain,
  indexable column is worth the extra columns.

### ID strategy: auto-increment integers
Primary keys are auto-increment integers, not UUIDs.
- **Why:** multi-user readiness (see [PROJECT.md](PROJECT.md) § Users)
  comes from `user_id` ownership/scoping and proper authorization checks
  on the relevant tables, not from ID type. UUIDs would add defense-in-
  depth against ID enumeration in URLs, but future multi-user here means
  friends/family sharing the app, not adversarial public users — so that
  extra guard isn't worth the larger/slower keys now.

### Cover art: hotlink external URL
`Item.cover_art_url` stores the URL returned by TMDB/OpenLibrary/IGDB at
add-time; images are loaded directly from the source's CDN, not
downloaded/stored locally.
- **Why:** zero storage/hosting cost and no extra step on add. Trade-off:
  the image breaks if the source moves/removes it, and the app depends on
  their uptime — acceptable for v1.

## Data Model

**User**
- `id`, `name`, `external_identity` (unique — the Easy Auth principal
  id this row maps to; see "User identity" above)
- Rows are auto-created on first login. v1 in practice holds a single
  row (the sole authorized user), but nothing assumes that.

**Item**
- `id`, `media_type` (TV / Film / Documentary / Book / Game), `title`,
  `cover_art_url`
- `external_source` (TMDB / OpenLibrary / IGDB), `external_id` — used for
  "already on Shelf" duplicate detection during search
- `status` (Backlog / InProgress / Finished)
- `recommendation_note` (nullable text) — current value
- `added_at`, `removed_at` (nullable — soft delete)
- Unique on `(external_source, external_id)` — backs the "already on
  Shelf" duplicate check on search (see [PROJECT.md](PROJECT.md) journey
  1)

**Genre**
- `id`, `name` (unique)

**ItemGenre** (join table)
- `item_id`, `genre_id`

**Review** — one per item
- `id`, `item_id`, `user_id`, `rating` (1–5, nullable), `text` (nullable),
  `created_at`, `updated_at`
- No separate `review_date` column — `created_at` **is** the review date
  shown in the UI ([PROJECT.md](PROJECT.md) § Rate and review). A
  deleted-then-re-added review is a new row (hard delete), so
  `created_at` always reflects when the current review was added.
- Check constraint: at least one of `rating`, `text` is non-null — an
  entirely empty review is not representable.
- Unique on `item_id` — enforces one review per item (v1); re-adding
  edits the existing row rather than inserting a new one. Would become
  unique `(user_id, item_id)` if multi-user lands.
- Hard-deleted when removed; history preserved via Event snapshot.

**Comment**
- `id`, `item_id`, `user_id`, `text`, `created_at`, `updated_at`
- Hard-deleted when removed; history preserved via Event snapshot.

`user_id` on Review and Comment mirrors the attribution groundwork on
Event ([PROJECT.md](PROJECT.md) § Users): v1 stamps the sole logged-in
user (see "User identity" above), and content rows carry ownership from
day one so multi-user is a constraint change, not a rewrite.

**Event** — drives the global Timeline, each item's detail-page timeline,
and the Stats feature
- `id`, `item_id`, `user_id`, `occurred_at`
- `type`: `item_added`, `item_removed`, `status_changed`, `review_added`,
  `review_edited`, `review_deleted`, `comment_added`, `comment_edited`,
  `comment_deleted`, `recommendation_note_changed`,
  `recommendation_note_removed`
- `from_status`, `to_status` (nullable, promoted columns — populated only
  for `status_changed` events; see "Event payload: hybrid" above).
  `to_status` drives Stats; `from_status` is display-only (lets the
  Timeline render "Backlog → Finished") and isn't queried.
- `payload` (nullable JSON) — snapshot data for all other event types,
  shape depends on `type` (e.g. `review_edited` → `{rating, text}`;
  `comment_deleted` → `{text}` as last-known content)

**Stats query shape:** `Event` rows where `type = status_changed`,
`to_status ∈ {InProgress, Finished}`, `occurred_at` within the selected
period, deduped per item by taking the furthest status reached in that
window (Finished takes precedence over In Progress).

## Implementation Notes

- **Transactional writes:** any action that touches both current state
  and the event log (e.g. status change = update `Item` + insert `Event`)
  must run in a single transaction — otherwise the Timeline and Stats can
  drift from what the Shelf shows.
- **Indexes on Event:** `(occurred_at)` for the global Timeline and
  `(item_id, occurred_at)` for the item detail-page timeline. Trivial at
  single-user scale, but free to add up front.

## Open Questions

None currently.
