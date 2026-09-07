# Shelf — Technical Design

This is the canonical reference for technical/architecture decisions for
Shelf. Product scope and behavior live in [PROJECT.md](PROJECT.md); this
doc covers how it's built. Updated as decisions are made.

## Status

**v1 is live and fully functional in production** (2026-07-24) —
https://shelf-app-ccbp.azurewebsites.net, behind Easy Auth. All 9
phases of [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) are
implemented, tested (26 xUnit tests), merged to `main`, and deployed:
identity, Shelf browse, search/add (TMDB/OpenLibrary/IGDB), the full
item lifecycle (status, reviews, comments, recommendation notes,
remove/re-add), Timeline, and Stats. Full prod smoke test passed
2026-07-24: sign-in, real user attribution, adding from all three
providers, and the complete item-detail flow all verified working live.

Three bugs were found only in production during this deploy (all
fixed; the first two are in git history on `develop`/`main`, the
third was a config change — see below):
- The deploy workflow's `dotnet build`/`dotnet publish` had no
  `--project` argument; once Phase 0 added a second project
  (`Shelf.Web.Tests`) to `Shelf.slnx`, this published both projects'
  output into one folder, and Azure's Oryx build system — finding two
  `.runtimeconfig.json` files — silently fell back to running its
  default placeholder app instead of erroring. Fixed by pointing both
  commands at `src/Shelf.Web/Shelf.Web.csproj` explicitly.
- `CurrentUserService`'s first-login auto-create ("does this user
  exist? no → insert") wasn't race-safe: two near-simultaneous requests
  on the very first real page load (each with its own DbContext) both
  found no row and both tried to insert, and the second hit
  `IX_Users_ExternalIdentity`'s unique constraint. Fixed by catching
  that specific failure and re-reading the row the other request
  committed.
- **Unhandled `SqlException` on database auto-resume** (issue #8,
  surfaced 2026-07-24, finally fixed and verified 2026-07-25). The
  first request after the free-tier database had auto-paused (see
  § Database) hit the generic ASP.NET error page instead of loading:
  an unhandled `SqlException` ("Connection Timeout Expired" during
  the post-login phase, ~29s, `Error Number: -2`) reached the page
  handler, since `AddDbContext` had no retry policy configured.
  This took three attempts to resolve, and the false starts are worth
  recording:
  - **Attempt 1 (2026-07-24, ineffective — never shipped).** Added
    `sqlOptions.EnableRetryOnFailure()` to the `UseSqlServer` call in
    `Program.cs` (`8ecf87b`). Issue #8 was closed on the strength of
    the commit alone. The commit sat unpushed on local `develop`,
    alongside `65812ce` (the "search all media types" feature,
    issue #6), so it never reached `main`/prod at all.
  - **Attempt 2 (2026-07-25, shipped but still ineffective).** Both
    commits pushed and merged to `main` via PR #13 (deploy run
    `30154598792`, 10:30 UTC). The bug reproduced anyway, twice, at
    12:49 and 13:18 UTC. Issue #8 was auto-closed a second time by
    the merge, again without verification.
  - **Why `EnableRetryOnFailure()` could never work here.** The
    exception reaching `ExceptionHandlerMiddleware` was a bare
    `SqlException`, not a `RetryLimitExceededException` — proof the
    retry strategy never engaged. Azure's docs state that connecting
    to a paused serverless database fails fast with **error 40613**,
    which *is* in EF Core's transient list; had the platform behaved
    that way the retry fix would have worked as intended. In practice
    the connection instead completes pre-login and login in under a
    second, then **hangs in the post-login phase** until the client
    timeout expires, surfacing as **error `-2`**, which EF Core
    deliberately does *not* treat as transient (a timeout can mean the
    operation actually succeeded server-side, so blind retry is
    unsafe). The fix was written against the documented failure mode;
    the platform produces a different one.
  - **Actual root cause.** The App Service connection string had
    `Connection Timeout=30`, while every measured auto-resume took
    **~60 seconds** (Azure activity log: 12:49:19→12:50:19 and
    13:18:18→13:19:18). The client was abandoning the attempt at
    ~29s, less than half way through a resume that has never been
    observed finishing in under a minute — so the first request after
    any pause was *guaranteed* to fail, not merely likely.
  - **Fix (2026-07-25):** raised `Connection Timeout` from 30 to
    **90 seconds** in the App Service `ShelfDb` connection string.
    Config-only; no code change. The connection now waits out the
    resume, so the cost is a slow first page load rather than an
    error. Verified in prod against a genuine cold start: database
    confirmed `Paused`, app restarted, Timeline page loaded in a
    browser, `resumedDate` 16:43:58 UTC, page rendered normally.
  - `EnableRetryOnFailure()` remains in `Program.cs`. It is harmless
    and no longer load-bearing. Optional further hardening — adding
    `errorNumbersToAdd: new[] { -2 }` — was deliberately **not**
    applied: the timeout change is verified sufficient, and making
    `-2` retryable risks re-applying a write that had already
    committed (e.g. a duplicate `Event` row). Revisit only if this
    recurs.
  - **Process lesson.** Issue #8 was closed twice before the fix was
    ever verified in production — once on an unpushed commit, once by
    an auto-close on merge. A bug is not fixed until it has been
    observed not happening in prod.

Local dev, Azure provisioning, and first deploy were completed
2026-07-22–23 (see § Local Development and § Provisioned
Infrastructure → Deployment for that history).

### Actions for next session
- No outstanding bug work. `main` and `origin/develop` are in sync
  and everything known is deployed; issues #8 and #14 are closed and
  verified.
- Next likely work is a UI redesign from wireframes (visual-layer
  only; `ShelfService`/`StatsService`/search providers wouldn't need
  to change) — no wireframes provided yet.
- Known open issue #12 ("Display release date for unreleased games"),
  unrelated to any of the above.

## Provisioned Infrastructure

All in resource group `shelf-rg`, subscription "Azure subscription 1"
(tenant `efb59d4c-9e6b-413c-8a20-a9d30be0abc5`). Running cost: £0/month.

- **SQL server:** `shelf-sql-ccbp.database.windows.net` (UK South),
  admin login `shelfadmin`. Password: stored in the App Service
  `ShelfDb` connection string (portal → web app → Environment
  variables) — copy to a password manager.
  Firewall: Azure services allowed + home IP (will need updating if
  the home IP changes).
- **Database:** `Shelf` — serverless free tier (`useFreeLimit: true`),
  10 GB / 100k vCore-seconds per month, **pauses** (not bills) on
  exhaustion (`freeLimitExhaustionBehavior: AutoPause`). Schema:
  `InitialCreate` applied 2026-07-22.
  - **Auto-pause behaviour (measured 2026-07-25, not as configured).**
    ARM reports `autoPauseDelay: 60`, but the database consistently
    pauses after **~25 idle minutes** (24.5 min across three measured
    cycles). The delay cannot be changed — Azure rejects any attempt
    with `ProvisioningDisabled: Only default value for auto pause
    delay is allowed for Free Limit database with auto pause
    exhaustion behavior`. The gap between the reported and enforced
    value looks like an Azure bug; it isn't actionable.
  - **Resume takes ~60 seconds**, every time (activity log:
    12:49:19→12:50:19, 13:18:18→13:19:18). This is why the client
    connect timeout is 90s — see § Web app and issue #8.
  - **The aggressive pause is load-bearing for staying free.** While
    online the database bills ~41 vCore-sec/min (min-capacity billing
    at 0.5 vCores), so the 100k monthly allowance buys roughly **40
    hours online per month**. At ~25 min per session that is ~99
    sessions/month; at a true 60-minute delay it would be ~40. Don't
    try to lengthen it even if Azure ever permits it. July 2026 usage
    was 16,541 of 100,000.
- **App Service plan:** `shelf-plan`, F1 (free), Linux, **UK West**
  (UK South had no free-tier VM quota).
- **Web app:** `shelf-app-ccbp` →
  https://shelf-app-ccbp.azurewebsites.net, runtime DOTNETCORE:10.0.
  Connection string `ShelfDb` (type SQLAzure) set in app config.
  - ⚠️ **`Connection Timeout=90` in that connection string is
    load-bearing — do not lower it.** It exists so the first request
    after the database auto-pauses can wait out the ~60s serverless
    resume instead of erroring (issue #8). The default of 30 is not
    enough and produces an unhandled `SqlException` on the error page.
  - This setting lives **only in App Service configuration** — there
    is no commit in the repo that records it, so `git log` won't
    reveal it. If the web app is ever recreated or its configuration
    reset, this must be reapplied or the bug returns.
- **Deployment:** GitHub Actions workflow
  `.github/workflows/main_shelf-app-ccbp.yml` on `main`, publish-
  profile secret in repo. Deploys on push to `main`. First deploy
  succeeded 2026-07-23.
  - **SCM basic-auth publishing: enabled** (2026-07-23). Azure
    disables it by default on new apps, which made the publish
    profile invalid and failed the first deploy attempt. Enabled it
    and stored a freshly issued publish profile in the repo secret
    (`AzureAppService_PublishProfile_662…`). Deliberate trade-off:
    keeping App Service's generated publish-profile workflow over
    migrating to OIDC federated credentials — simpler, and fine for
    a single-owner hobby app. If publishing credentials are ever
    reset, refresh the secret the same way
    (`az webapp deployment list-publishing-profiles --xml` →
    `gh secret set`).
- **Easy Auth:** enabled, all requests require sign-in. Entra app
  registration "Shelf Easy Auth" (client id
  `521b55e5-2962-4cbb-81b7-c3bb7d4582e9`), sign-in audience
  AzureADMyOrg (owner's directory only — effectively locked to Tom).
  **Client secret expires July 2028** — sign-in breaks then unless
  rotated.

## Local Development

Set up and verified 2026-07-23 on the dev laptop.

- **Database: SQL Server Express LocalDB** (2022, v16.0.1000.6),
  instance `MSSQLLocalDB` — installed from Microsoft's `SqlLocalDB.msi`.
  On-demand (no always-running service); same engine family as Azure
  SQL, so the one set of EF migrations serves both. Chosen over
  pointing dev at Azure SQL (would burn free-tier vCore-seconds, hit
  auto-pause cold starts, and mix dev experiments into live data) and
  over Docker/SQLite (heavier / provider divergence, respectively).
- **Connection:** `appsettings.Development.json` points `ShelfDb` at
  `(localdb)\MSSQLLocalDB`, database `Shelf`. The `InitialCreate`
  migration is applied. Production's connection string lives only in
  App Service config, so local and Azure can't be confused.
- **Workflow:**
  - `dotnet tool restore` (once per clone) — restores `dotnet-ef` from
    the repo tool manifest.
  - `dotnet ef database update --project src\Shelf.Web` — applies
    pending migrations locally (`dotnet ef` defaults to the Development
    environment, so it targets LocalDB).
  - `dotnet watch --project src\Shelf.Web` — run with hot reload at
    `http://localhost:5186` (profiles in `Properties/launchSettings.json`;
    the https profile also serves `https://localhost:7214`).
- **Easy Auth locally (decision, not yet implemented):** the upcoming
  user-identity middleware reads the `X-MS-CLIENT-PRINCIPAL` headers
  Easy Auth injects in Azure. Those headers don't exist on localhost
  (no login wall), so in the Development environment the middleware
  falls back to a fixed dev identity (`external_identity = "local-dev"`,
  name "Tom"), auto-creating that User row locally on first use — the
  attribution path is exercised locally without simulating a login.

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
