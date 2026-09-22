# Conversation Filters — Design

**Status:** Approved by user, ready for implementation planning.

## Goal

Add user-defined, taggable "filters" (like Gmail labels) for conversations:
create/rename/recolor/delete filters, assign multiple filters to a
conversation from the existing multi-select toolbar, and browse the
conversation list narrowed to one or more filters from a new header icon.

## Background

The Conversations screen already has one user-defined per-thread flag,
Favorite (`FavoriteThread` table, `IFavoriteRepository`), and a header
toggle for a built-in one, unread-only (`ShowUnreadOnly`,
`ConversationsViewModel.ApplyFilter()`). Filters generalize the Favorite
pattern (arbitrary user-created categories instead of one fixed flag) and
sit alongside the unread-only toggle in both the header and
`ApplyFilter()`'s narrowing logic.

## Out of scope

No nested/hierarchical filters, no per-filter notification behavior, no
filter sharing/sync across devices, no full custom color picker (a small
preset swatch set only). Filters never affect Android's own SMS provider —
same "app-local overlay, never touches source of truth" rule the rest of
this app follows for Favorite/Trash/Archive/Blocked.

## Data layer

Two new tables in the existing `ForgeLinkSms.db` (`sqlite-net-pcl`), via a
new `IFilterRepository`/`FilterRepository` registered in `MauiProgram.cs`
exactly like `FavoriteRepository`/`ArchiveRepository`:

```
Filter
  Id         INTEGER PRIMARY KEY AUTOINCREMENT
  Name       TEXT NOT NULL
  ColorHex   TEXT NOT NULL        -- e.g. "#6366f1"

ThreadFilterAssignment
  Id         INTEGER PRIMARY KEY AUTOINCREMENT
  ThreadId   INTEGER NOT NULL     -- Android's SMS thread_id
  FilterId   INTEGER NOT NULL     -- Filter.Id
```

A conversation can have any number of filters (many-to-many). Filter names
are not required to be unique — color is what visually disambiguates two
similarly-named filters. Assignments are keyed by `ThreadId` only, so they
are untouched by trash/archive/restore, the same way `FavoriteThread` rows
already survive those flows.

`IFilterRepository` surface:

```csharp
Task InitializeAsync();
Task<IReadOnlyList<Filter>> GetAllFiltersAsync();
Task<Filter> CreateFilterAsync(string name, string colorHex);
Task RenameFilterAsync(long filterId, string newName);
Task SetFilterColorAsync(long filterId, string colorHex);
Task DeleteFilterAsync(long filterId); // also deletes its ThreadFilterAssignment rows
Task<IReadOnlyDictionary<long, List<long>>> GetAllAssignmentsAsync(); // threadId -> filterIds, one bulk query for Load()
Task AssignFilterAsync(long threadId, long filterId);
Task UnassignFilterAsync(long threadId, long filterId);
```

`SmsThread` gets a new `IReadOnlyList<long> FilterIds` property, populated
in `ConversationsViewModel.Load()` from `GetAllAssignmentsAsync()` the same
way `IsFavorite` is populated today from `GetFavoriteThreadIdsAsync()`.

## ConversationsViewModel changes

- New `Filters` collection (all defined filters, loaded in `Load()`) — used
  by the picker sheet and the row color dots.
- New `ActiveFilterIds` (`HashSet<long>`) — which filters are active for
  *browsing*, mutated only through `ToggleActiveFilter(long filterId)`
  (which also calls `ApplyFilter()`).
- `ApplyFilter()` gains a step: when `ActiveFilterIds` is non-empty, keep
  only threads whose `FilterIds` intersects it (OR across selected
  filters), applied in the same chain as the existing `ShowUnreadOnly` and
  search-text narrowing (AND across all three).
- `AllSelectedHaveFilter(IReadOnlyList<long> threadIds, long filterId)` —
  true only if every thread in the multi-selection already has that
  filter; drives the assign-sheet's checkbox state.
- `ToggleFilterForThreadsCommand(IReadOnlyList<long> threadIds, long
  filterId)` — if `AllSelectedHaveFilter` is true, unassigns the filter
  from every thread in the selection; otherwise assigns it to every thread
  that doesn't already have it. Applied instantly per tap, no separate
  confirm step.
- Deleting a filter (via the maintenance page) also removes it from
  `ActiveFilterIds` if present, so the list can't stay stuck filtered on a
  filter that no longer exists.

## Shared component: `FilterPickerSheet.razor`

Same bottom-sheet look and same hardware-back handling as
`AttachmentDrawer` (`IsOpen`/`Open()`/public `Close()`, wired into
`NavigationHistoryTracker.LocalBackHandler` in `ConversationsPage`'s
`OnInitializedAsync`/`Dispose`, so a back-press while the sheet is open
closes the sheet instead of leaving the Conversations screen — the exact
bug class fixed for `AttachmentDrawer` earlier this project).

Parameters:

```csharp
[Parameter] public IReadOnlyList<Filter> Filters { get; set; }
[Parameter] public Func<Filter, bool> IsChecked { get; set; }
[Parameter] public EventCallback<Filter> OnToggle { get; set; }
[Parameter] public bool ShowManageLink { get; set; } // true only for the header/browse call site
[Parameter] public EventCallback OnManageFilters { get; set; }
```

Each row: a colored circle swatch (`Filter.ColorHex`) + name + a checkbox
reflecting `IsChecked(filter)`. Tapping a row calls `OnToggle`. When
`ShowManageLink` is true, a trailing "Manage filters" row navigates to
`/filters` (closing the sheet first).

Two call sites:

- **Header funnel icon** (browse mode): `IsChecked` = `ActiveFilterIds`
  contains the filter; `OnToggle` = `ToggleActiveFilter`; `ShowManageLink`
  = true.
- **Multi-select toolbar's new funnel button** (assign mode): `IsChecked` =
  `AllSelectedHaveFilter(_selectedThreadIds, filter)`; `OnToggle` =
  `ToggleFilterForThreadsCommand(_selectedThreadIds, filter)`;
  `ShowManageLink` = false.

## Header & row UI

- `ConversationsPage`'s left-justified header group becomes: eye (unread) →
  funnel (filter) → search. The funnel button gets the same active-state
  treatment as the eye toggle: accent-colored background when
  `ActiveFilterIds.Count > 0`, neutral otherwise. Icon is an inline SVG
  funnel glyph, matching the eye icons' style (`currentColor` stroke, same
  size).
- Multi-select toolbar order becomes: `✕ · count · ❤️ · 🏷️(new) · 👁️ · 📦 ·
  🗑 · ⋮`.
- Each thread row renders a small colored dot per assigned filter
  (resolved via `ViewModel.Filters`), placed next to the existing ❤️
  favorite marker.

## Filter Maintenance page (`/filters`)

Reached from a new "🏷️ Filters" row in `MenuPage.razor`'s list (after
Blocked, before Scheduled — matches that list's existing ordering
convention of destinations before actions), and from the header picker
sheet's "Manage filters" link. Kept in the same plain, unstyled list
convention as `ArchivedPage`/`BlockedPage` rather than
`ConversationsPage`'s richer inline styling:

- An add-filter row: text input + a fixed row of 8 preset color swatches
  (`#ef4444` red, `#f97316` orange, `#eab308` yellow, `#22c55e` green,
  `#06b6d4` cyan, `#6366f1` indigo, `#a855f7` purple, `#ec4899` pink — no
  custom color entry) + "Add" button, disabled on a blank name or no color
  selected; first swatch pre-selected by default.
- Each existing filter, one row: color swatch (tap → reopen the same
  8-swatch picker to change it), name (tap → inline rename), and a 🗑
  delete button that acts immediately — no confirmation dialog, matching
  this page family's existing Unblock/Unarchive one-tap convention.
  Deleting a filter deletes its `ThreadFilterAssignment` rows too.

New `FiltersViewModel` (`Load`, `CreateFilterCommand`,
`RenameFilterCommand`, `SetFilterColorCommand`, `DeleteFilterCommand`),
same shape as `ArchivedViewModel`/`BlockedViewModel`.

## Testing

- `ForgeLinkSms.Core.Tests` gets a new `FilterRepositoryTests.cs` (against
  a real temp-file SQLite database, same convention as
  `FavoriteRepositoryTests.cs`/`ArchiveRepositoryTests.cs`): CRUD on
  `Filter`, assign/unassign, `GetAllAssignmentsAsync()` shape, and that
  deleting a filter removes its assignment rows.
- `ConversationsViewModelTests.cs` gets new cases: `ApplyFilter()`'s OR
  semantics across multiple active filters, AND-stacking with
  `ShowUnreadOnly`/search, `AllSelectedHaveFilter` correctness for
  mixed-assignment selections, and `ToggleFilterForThreadsCommand`'s
  assign-vs-unassign branching.
- New `FiltersViewModelTests.cs`, mirroring `ArchivedViewModelTests.cs`/
  `BlockedViewModelTests.cs`.
- Manual, on-device verification (this project's established pattern — no
  working emulator on this machine): create a couple of filters, assign
  them to several threads via multi-select, confirm row dots and header
  browse-by-filter narrowing both work, confirm hardware back closes the
  picker sheet without leaving Conversations, delete a filter and confirm
  it disappears from rows/picker/`ActiveFilterIds` immediately.

## Open risks

- None identified that block implementation — this is a straightforward
  extension of an existing, proven pattern (Favorite) rather than new
  architecture.
