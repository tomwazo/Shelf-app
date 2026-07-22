# Shelf

> Technical/architecture decisions (data model, stack, etc.) live in
> [TECHNICAL_DESIGN.md](TECHNICAL_DESIGN.md) — this doc covers product
> scope and behavior only.

## Purpose

A personal media tracker for logging what I've watched, played, and read —
across TV shows, films, documentaries, books, and computer games — along with
what I thought of it. It should also let me log things I want to consume but
haven't got round to yet, so I have a single place to track both what I've
already experienced and what's still ahead of me. Solves the problem of
forgetting past viewing/reading/playing history and my opinions on it, and
gives me a single backlog to browse when deciding what to watch/play/read
next.

## Users

- **Primary (v1):** Just me — single-user app, no in-app account system
  (sign-in is handled at the hosting level; see TECHNICAL_DESIGN.md).
- **Future (not v1):** Possible expansion to friends/family as additional
  users. The data model should avoid assumptions that make this a rewrite
  later (e.g. don't hard-code "there is exactly one user"), but no
  multi-user features are being built now.
- **Groundwork — user attribution on events:** every logged event (added/
  removed from Shelf, status change, review, comment, recommendation note)
  records which user performed it.
  - v1 has no in-app accounts, but the app sits behind a hosting-level
    login (see TECHNICAL_DESIGN.md § User identity), and each event is
    stamped with the actual logged-in user — real attribution, not a
    placeholder. Access stays restricted to one user in v1; what
    multi-user *means* for the data (shared vs per-user shelves, etc.)
    remains a deferred question.
  - Shown on Timeline entries (e.g. "changed status to Finished — Tom"),
    even though v1 only ever shows the one user.

## Platform

Web app, accessed via browser (desktop and phone).

## Core Features (v1)

### 1. Shelf (browse view)
- Shows everything I've added, across all statuses (Backlog, In Progress,
  Finished).
- **Filter by:**
  - Media type (TV, Film, Documentary, Book, Game).
  - Genre.
  - Status — including a quick filter for "consumed" vs "not consumed."
- **Sort by:**
  - Date added.
  - Alphabetical (A–Z or Z–A).
- **Counts:** Shows total item count (e.g. "42 items on your Shelf"), updating
  to reflect the currently applied filters (e.g. "12 films" when filtered to
  Film).
- Empty state (no items, or none matching the current filters): "No items
  to display."

### 2. Timeline
- View activity over time. Event types: added to Shelf, removed from
  Shelf, status change, review (add/edit), comment, recommendation note
  (add/edit).
- Filterable by period (past 24 hours, week, month, etc) — defaults to
  past 7 days on landing.
- All event types shown together by default, with the ability to filter
  down to a specific event type.
- Clicking a Timeline entry navigates to that item's detail page.
- Timeline is the default landing page when opening the app.
- The item detail page is itself structured as a timeline, scoped to that
  item: added event, status changes, reviews, comments, and recommendation
  note changes, in chronological order. These same events also appear on
  the global Timeline.
- Empty state (no events in the selected period): "No items to display."

### 3. Add items via external database search
- Search and add items by media type:
  - Films/TV — TMDB
  - Books — OpenLibrary
  - Games — IGDB
- Selecting a search result auto-fills title, cover art, and genre.
- Available from both the Shelf page and the Timeline.

### 4. Track status
Each item has one of the following statuses:
- Backlog
- In Progress
- Finished
- Status is changed only from the item's detail page.
- Any status transition is allowed, including Backlog → Finished directly
  (no requirement to pass through "In Progress").
- Every status change is logged as a Timeline event.

### 5. Rate and review
- Star rating: 1–5 stars.
- Free-text review.
- Review date (recorded automatically when the review is added).
- Optional — an item can be "Finished" with no rating/review.
- One review per item (v1) — adding a review again later edits the
  existing one rather than creating a new entry.
- Reviews can also be deleted entirely, not just edited/overwritten.
- Adding, editing, or deleting the review is logged as its own event, on
  both the item's detail-page timeline and the global Timeline.

### 6. Comments
- Free-text comments against an item, timestamped.
- Multiple comments per item, added at any time regardless of status —
  for ad-hoc observations/thoughts as they occur, separate from the single
  formal review.
- Comments can be edited or deleted after posting.
- Comments appear on both the item's detail-page timeline and the global
  Timeline.

### 7. Tagging
- Genre tags (pulled from external database on add). Editing tags after
  add is deferred to a future iteration — see Out of Scope.

### 8. Recommendation note
- Optional free-text note per item flagging who recommended it (e.g.
  "Recommended by Dan"), separate from the eventual review.
- Shown on the item's detail page only (v1) — not surfaced on the Shelf
  card/list.
- Can be added, edited, or removed at any time (not just at add-time).
- Adding or editing it is logged as its own event, on both the item's
  detail-page timeline and the global Timeline.

### 9. Stats
- A standalone page, reachable from nav alongside Shelf and Timeline, for
  reviewing consumption activity over a selected period.
- **Period selection:** presets only (e.g. past week, month, year, all
  time) — no custom date range in v1.
- **Inclusion rule:** an item counts in a period if its status changed to
  In Progress or Finished at any point during that period — this tracks
  activity within the window, not current status.
  - An item that transitions through multiple statuses within the same
    period (e.g. Backlog → In Progress → Finished all within the window)
    is counted once, under the furthest status reached — Finished takes
    precedence over In Progress.
- **Numeric stats:** total count for the period, broken down by media
  type (e.g. "5 films, 3 books, 2 games"), split by In Progress vs
  Finished.
- **Item list:** alongside the counts, the items themselves are listed,
  so you can see exactly what was started/finished in the period, not
  just how many.
- Empty state: a period with no started/finished items shows "No items
  to display."

### 10. Remove item
- Removes an item from the Shelf. Only available from the item's own
  detail page (no inline remove from the Shelf list/cards).
- Soft delete — the item is hidden from the Shelf, but its full history
  (events, reviews, comments) is preserved, not deleted.
- Logged as a "removed from Shelf" event on the global Timeline.
- A removed item can be re-added later via search; this restores it with
  its history and prior status intact (see journey 1).

## User Journeys

1. **Add something to the backlog** — search external DB → select result →
   auto-filled item lands on Shelf with status "Backlog," from either the
   Shelf page or Timeline.
   - Auto-filled fields (title, cover art, genre) are not editable — in
     v1 there's no edit-metadata flow at all; that's deferred to a future
     iteration (see Out of Scope).
   - No search results: display a "no match" message. There's no manual/
     custom add fallback in v1 — this is a dead end if the item isn't in
     TMDB/OpenLibrary/IGDB (see Out of Scope).
   - Result already on the Shelf: shown in results with a note indicating
     it's already added, and not selectable. Previously *removed* items
     don't count as already added — they show as normal, selectable
     results.
   - Re-adding a previously removed item restores the original item —
     with its prior status, review, comments, and full history — rather
     than creating a fresh entry. A new "added to Shelf" event is logged.
   - Status always defaults to "Backlog" on add — no option to add straight
     into "In Progress"/"Finished." (Exception: restoring a previously
     removed item keeps its prior status — see above.)
2. **Start consuming an item** — find it on Shelf (or via Timeline) → open
   item's detail page → change status to "In Progress."
   - Status can only be changed from the item's detail page.
   - Backlog → Finished directly is also allowed (skipping "In Progress").
   - The status change is logged as an event on both the global Timeline
     and the item's own detail-page timeline.
3. **Log a thought mid-consumption** — open item's detail page → add a
   comment.
   - Comments can be added regardless of status (Backlog, In Progress, or
     Finished) — not gated on being "In Progress."
   - Comments can be edited or deleted after posting.
   - Comments appear on both the item's detail-page timeline and the
     global Timeline.
4. **Finish and review** — change status to "Finished" → optionally add
   star rating + review (review date auto-set).
   - Review is optional — an item can stay "Finished" with no rating/review.
   - One review per item (v1); adding again edits the existing review.
   - Adding/editing the review is logged as its own event, on both the
     item's detail-page timeline and the global Timeline.
5. **Decide what to consume next** — browse Shelf → filter by type/genre/
   status ("Backlog"/"not consumed") → sort → pick from backlog.
   - No dedicated "Backlog only" shortcut — the existing status filter
     covers this.
   - Recommendation note ("Recommended by...") is shown on the item's
     detail page only, not on the Shelf card — open the item to see it.
   - No other extra Shelf-card info (e.g. ratings, tags) in v1 — cards show
     only what's already specified (title, cover art, etc).
6. **Catch up on recent activity** — land on Timeline (defaults to past 7
   days) → filter by period/event type → see what's happened.
   - Event types: added to Shelf, removed from Shelf, status change,
     review, comment, recommendation note.
   - Clicking an entry navigates to that item's detail page.
7. **Log a recommendation** — open item's detail page → note who
   recommended it, independent of review.
   - Can be added, edited, or removed at any time — not limited to
     add-time.
   - Logged as its own event on both the item's detail-page timeline and
     the global Timeline.
8. **Review consumption over a period** — open Stats page → pick a preset
   period → see total count broken down by media type (In Progress vs
   Finished) and the list of items that were started/finished in that
   window.
   - An item is counted once, under the furthest status it reached within
     the period (Finished takes precedence over In Progress).
9. **Remove an item** — open item's detail page → remove from Shelf.
   - Only available from the item's detail page, not inline from the
     Shelf list/cards.
   - Soft delete — item is hidden from the Shelf, but its history
     (events, reviews, comments) is preserved.
   - Logged as a "removed from Shelf" event on the global Timeline.
   - Re-adding it later via search restores the original item with its
     history and prior status (see journey 1).

## Out of Scope (v1)

- Multi-user accounts, sharing, or permissions.
- Recommendation engine / "what should I consume next" beyond backlog
  filtering.
- Import/replace existing tools (Letterboxd, Goodreads, Backloggd, Trakt) —
  not currently used, no migration needed.
- Native mobile app (browser-based only for now).
- Prioritization/ordering within the backlog (flags, priority levels, manual
  reordering) — sort by date added/alphabetical covers this for now.
- Inline actions (status change, remove) from the Shelf view — all edits
  happen on the item's detail page.
- Custom date ranges for Stats — preset periods only for now.
- Genre/rating breakdowns on the Stats page — v1 is counts by media type
  only.
- Editing item metadata (title, cover art, genre) after add — fields are
  fixed at add time in v1; deferred to a future iteration.
- Manual/custom item entry when a search returns no match — v1 requires
  items to exist in TMDB/OpenLibrary/IGDB; no fallback for items that
  aren't in those databases.
