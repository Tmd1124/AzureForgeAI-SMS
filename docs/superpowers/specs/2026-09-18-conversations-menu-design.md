# Conversations Menu, Theme, Profile, Trash, Blocked, and Undo — Design

**Status:** Approved by user, ready for implementation planning.

## Goal

Replace the bottom `Messages` / `Settings` nav bar with a Google-Messages-style
top-right menu on the Conversations screen, and add the six destinations behind
it: Message Settings (existing), Theme, Your Profile, Trash, Blocked, Mark as
Read, and Undo.

## Background

The original plan (`docs/superpowers/plans/2026-09-17-sms-messenger.md`, Task
12) shipped a simple bottom nav bar (`NavLink`s for `/conversations` and
`/settings`). The user wants that replaced with a single top-right icon that
opens a full menu screen, matching a Google Messages reference screenshot
supplied during design (profile card up top, a plain list of icon+label rows
below). This also introduces three genuinely new subsystems the original plan
never scoped: soft-delete (Trash), contact blocking, and a cross-feature Undo
stack — all confirmed by the user to need real persisted/queryable state,
not just the app's existing `Preferences` flags.

## Out of scope

Matching the original plan's spirit, this design does **not** add: an
"Archived" section, "Device pairing," "Your data in Messages," or "Help &
feedback" (all present in the Google Messages reference screenshot, but not
requested). Undo does **not** cover sending/receiving messages themselves —
those are real network events. Trash and Blocked operate on whole threads,
not individual messages within a thread.

## Data layer

### New SQLite database

A new `SmsMessenger.db` (via the `sqlite-net-pcl` package), opened from
`SmsMessenger.Core` through a small `IAppDatabase`-style service, holding
exactly two tables — the only new state that is genuinely a queryable
collection rather than a single scalar:

```
TrashedThread
  ThreadId      INTEGER PRIMARY KEY   -- Android's SMS thread_id
  TrashedAtUtc  TEXT NOT NULL

BlockedNumber
  PhoneNumber   TEXT PRIMARY KEY      -- raw digits, same form used elsewhere in the app
  BlockedAtUtc  TEXT NOT NULL
```

Trashing a thread **never deletes rows from Android's SMS provider** — it
only inserts into `TrashedThread`. `ConversationsViewModel` and
`ThreadDetailViewModel`'s underlying queries exclude any thread ID present in
`TrashedThread`; the new Trash screen queries the opposite way (only IDs
present in the table). Restoring is deleting the row. Nothing is ever
destroyed by this feature, and Android's SMS provider remains the single
source of truth for message content — consistent with the rest of the app's
architecture (see the original plan's Global Constraints).

Blocking a number follows the identical filtering pattern via
`BlockedNumber`, plus writing an entry to Android's `BlockedNumberContract`
content provider (`content://com.android.blockednumber/blocked`) so the
platform itself stops delivering future texts from that number — the app is
the default SMS app, which grants write access to this provider. Unblocking
removes both the local row and the `BlockedNumberContract` entry.

### Preferences additions (no new table)

Everything else is a single scalar value and stays in `Preferences`,
alongside the existing onboarding-complete / notifications-enabled / SIM
choice entries:

- `theme_mode` — `"Light"` | `"Dark"` | `"System"`
- `theme_accent` — hex color string
- `profile_display_name` — string
- `profile_photo_path` — local file path (see Your Profile below)

### Undo stack

In-memory only, per the user's explicit call — cleared on app restart. A
`IUndoStack` singleton in `SmsMessenger.Core` holds a `Stack<IUndoableAction>`
(or an equivalent record + delegate pair). `IUndoableAction` exposes a
human-readable `Description` (for the "Undid: …" confirmation) and an
`UndoAsync()` method. Exactly three action types push onto it: trashing a
thread, blocking a number, and the global mark-all-as-read.

## Navigation shell

`Components/Layout/BottomNav.razor` and its usages in `ConversationsPage`
and `SettingsPage` are removed. Conversations becomes the sole main screen.
Its top-right corner gets a circular icon:

- Shows the user's profile photo (`profile_photo_path`) if one is set.
- Otherwise shows a gear icon.
- Tapping it navigates to a new `/menu` route.

`/menu` (`MenuPage.razor`) is modeled directly on the supplied Google
Messages screenshot: a header card (profile photo + display name, tappable →
`/profile`), then a plain vertical list of icon+label rows:　Theme, Your
Profile, Trash, Blocked, Mark as Read, Undo, Message Settings. Tapping
Theme/Your Profile/Trash/Blocked/Message Settings navigates to that
sub-page. Mark as Read and Undo execute immediately in place, show a brief
inline confirmation text, then return to `/conversations`.

## The menu items

### Message Settings

No new logic. `SettingsPage.razor` (already built in the original plan's
Task 11) is linked from `/menu` instead of the removed bottom nav.

### Theme

`/theme` (`ThemePage.razor`): three radio options (Light / Dark / System)
plus a row of color swatches, matching the Google Messages reference.
Selecting an option writes `theme_mode`/`theme_accent` to Preferences and
applies immediately. This requires a real (small) refactor: every existing
page's hardcoded colors (e.g. Splash's `#25D366`, the Compose FAB's
`#25D366`) move into CSS custom properties (`--accent-color`, plus a
`data-theme` attribute on `<body>` driving light/dark rules) defined once in
`app.css`, so the whole app reacts to a theme change consistently.

### Your Profile

`/profile` (`ProfilePage.razor`): a text field for display name, and a
tappable photo that opens the device's photo picker via
`MediaPicker.PickPhotoAsync()` (Android's modern Photo Picker — no new
runtime permission needed beyond what's already granted). The picked photo
is copied into app-private storage (`FileSystem.AppDataDirectory`) so it
survives the OS cleaning up the picker's temp file, and that path is saved
to `profile_photo_path`. Both fields save to Preferences on a Save action.

### Trash

`/trash` (`TrashPage.razor`): lists trashed threads (resolved contact name
or address, last message preview, trashed date), each with a "Restore"
button that deletes the `TrashedThread` row. Threads are trashed via the new
long-press action sheet on Conversations rows (below).

### Blocked

`/blocked` (`BlockedPage.razor`): same shape as Trash, for `BlockedNumber`
rows, each with an "Unblock" button that removes both the local row and the
`BlockedNumberContract` entry. Same long-press sheet offers "Block."

### Long-press action sheet (new, shared)

Long-pressing a row in `ConversationsPage` opens a small action sheet with
two options: "Trash" and "Block." This replaces separately-designed swipe
and long-press triggers considered during design — one shared entry point
covers both actions and needs no custom swipe-gesture/pointer-tracking code,
just a long-press detector (a simple pointerdown/timer/pointerup pattern,
since HTML has no native long-press event). Both actions push an entry onto
the undo stack before acting, then refresh the Conversations list (the
thread disappears immediately, matching the filter in the data layer
above).

### Mark as Read

Menu row executes immediately: marks every thread's messages `read=1` in
Android's SMS provider (global, not per-thread, per the user's confirmed
scope), shows "Marked N threads as read," pushes one undo entry recording
which thread IDs were previously unread, and returns to Conversations.

### Undo

Menu row pops the top entry off `IUndoStack` and calls its `UndoAsync()` —
un-trashing a thread, unblocking a number, or re-marking the specific
threads a mark-as-read touched back to unread — shows "Undid: …", and is
immediately pressable again for the next entry. Empty stack → row is
disabled or shows "Nothing to undo."

## Testing

- `SmsMessenger.Core.Tests` gets new unit tests for: `IUndoStack` push/pop
  ordering and multi-undo behavior: each `*ViewModel` that needs the new
  trash/blocked filtering (`ConversationsViewModel`, a new
  `TrashViewModel`/`BlockedViewModel`) against mocked services, exactly like
  every existing view model in this codebase.
- The SQLite-backed service implementations (`ITrashRepository` for
  `TrashedThread`, `IBlockedNumberRepository` for `BlockedNumber`) live in
  `SmsMessenger.Core` behind an interface each, following the same one
  interface per concern convention as `IContactService`/`IThreadService`/
  `ISmsService`. Unlike the `Platforms/Android/` services, `sqlite-net-pcl`
  is plain, platform-agnostic .NET, so these two are unit-tested directly
  against a real temp-file SQLite database rather than mocked — the
  original plan kept `SmsMessenger.Core` Android-free for exactly this kind
  of testability, and this stays consistent with it.
- Manual, on-device verification (per this project's established pattern —
  no working emulator on this machine) for: `BlockedNumberContract`
  integration, `MediaPicker` photo selection, theme color application across
  every existing page, and the long-press action sheet gesture.

## Open risks

- `BlockedNumberContract` write access as the default SMS app is expected to
  work but hasn't been verified on-device yet; if it turns out to require an
  additional permission or behaves differently on this device's Android
  version, Blocked falls back to app-only filtering (still fully
  functional, just not OS-enforced) as a documented fallback rather than a
  blocker.
- Retrofitting theme CSS variables across every existing page (Splash,
  Onboarding, Conversations, Thread Detail, Compose, Contact Picker,
  Settings) touches a lot of files; this is a real but mechanical refactor,
  not a design risk.
