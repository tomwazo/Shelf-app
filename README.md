# Shelf

A personal media tracker for logging what I've watched, played, and read —
TV shows, films, documentaries, books and computer games — along with what I
thought of it, plus a backlog of the things I haven't got round to yet.

It solves two problems at once: forgetting what I've already consumed and
what I made of it, and having nowhere single to look when deciding what to
watch, read or play next.

Single-user web app (desktop and phone browsers), built with ASP.NET Core
Razor Pages and deployed to Azure App Service behind a Microsoft sign-in
wall.

## What it does

- **Shelf** — browse everything added, filtered by media type, genre and
  status (with a "consumed vs not consumed" quick filter), sorted by date
  added or alphabetically. The item count tracks the active filters.
- **Timeline** — the landing page: a reverse-chronological feed of
  everything that's happened (items added and removed, status changes,
  reviews, comments, recommendation notes), filterable by period and event
  type. Every entry links through to the item.
- **Add by search** — search TMDB (film/TV), OpenLibrary (books) and IGDB
  (games) in one go, results grouped by media type. Picking a result
  auto-fills title, cover art and genres. Items already on the Shelf are
  flagged and unselectable; if one provider is down the others still return
  results.
- **Track status** — every item is Backlog, In Progress or Finished. Any
  transition is allowed, and each one is logged.
- **Rate and review** — one 1–5 star rating and free-text review per item,
  both optional; editable and deletable.
- **Comment** — as many timestamped notes per item as you like, at any
  status, separate from the formal review.
- **Recommendation notes** — record who suggested something ("Recommended
  by Dan"), independent of what you eventually thought of it.
- **Stats** — for a chosen period (week, month, year, all time): how many
  items were started or finished, broken down by media type, plus the list
  of exactly which ones.
- **Remove** — a soft delete. The item disappears from the Shelf but keeps
  its full history, and re-adding it later restores that history and its
  previous status rather than starting over.

## How it's built

- **ASP.NET Core Razor Pages** — server-rendered, essentially no
  JavaScript. Shelf is a CRUD app (lists, detail pages, forms), which is
  what Razor Pages is good at.
- **EF Core** over **Azure SQL Database** (serverless free tier). SQLite was
  the instinct for a single-user app, but App Service's persistent storage
  is an SMB file share, where SQLite's locking is a corruption risk.
- **Event log.** Current-state tables hold what's true now; a separate
  append-only `Event` table records what happened, and drives both the
  Timeline and Stats. Events store an immutable *snapshot* of the content at
  the time of the action, so editing or deleting a review or comment doesn't
  rewrite how past history reads.
- **Identity via Easy Auth.** App Service's built-in authentication is the
  login wall; the app maps the authenticated principal to a `User` row and
  stamps real attribution on every event. Only one user is authorized, but
  nothing in the schema assumes that.
- **Tested** with xUnit against SQLite in-memory — chosen over EF InMemory
  because it actually enforces the unique indexes and check constraints the
  interesting cases depend on.

## Running it locally

Needs the .NET SDK and SQL Server Express LocalDB.

```powershell
.\run-local.ps1
```

That restores tools, applies migrations to LocalDB and starts the app with
hot reload at http://localhost:5186. Locally there's no sign-in wall — the
app falls back to a fixed development identity.

Search against TMDB and IGDB needs API keys, set once as user secrets (see
the notes at the top of `run-local.ps1`). Without them OpenLibrary book
search still works and the other providers report themselves unavailable.

## Repository layout

```
src/Shelf.Web/         The app
  Identity/            Easy Auth principal -> User row
  Services/            Mutations (ShelfService), stats, event display
  Search/              TMDB / OpenLibrary / IGDB providers
  Pages/               Timeline, Shelf, Add, Stats, Items/Details
  Data/, Migrations/   EF Core context and schema
tests/Shelf.Web.Tests/ xUnit tests
```

## Documentation

- [project.md](project.md) — product scope and behavior: features, user
  journeys, what's deliberately out of scope for v1.
- [TECHNICAL_DESIGN.md](TECHNICAL_DESIGN.md) — the canonical architecture
  reference: stack and hosting decisions, data model, infrastructure, and
  the reasoning behind them.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) — the phased plan v1 was
  built to.

## Status

v1 is complete and running in production. It's a personal project — the
deployment is locked to a single account, so there's nothing publicly
reachable to look at.
