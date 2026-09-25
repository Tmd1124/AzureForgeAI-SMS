# Bubble Pond — Design

**Status:** Approved by user, ready for implementation planning.

## Goal

Make ForgeLink's main screen look unlike any other messaging app with a playful
"Bubble Pond": the people you care about most float as animated photo bubbles
at the top of the Chats tab, with the ordinary conversation list below them.

## Background

The Conversations screen (`ConversationsPage.razor` + `ConversationsViewModel`)
has three lanes (Chats / Updates / Screener), a codes strip, search, unread and
filter toggles, swipe-to-trash/archive, long-press multi-select with a ⋮ action
sheet, and an undo toast. Each `SmsThread` already carries `PhotoUri` (the
contact photo), `DisplayName`, `Initials`, `IsFavorite`, `UnreadCount` and
`LastMessageTimestamp`, so the pond needs no new data from Android.

Chosen in brainstorming (visual companion): layout **A — Bubble Pond**,
arranged as **pond on top, list below**, with **photo bubbles**.

## Out of scope

No physics or drag-to-rearrange, no per-person custom colors or bubble sizes,
no pond on the Updates or Screener lanes, no message-frequency statistics (size
comes from rank, not message counts), and no change to the conversation thread
screen.

## When the pond shows

Only when all of these hold: lane is Chats, search text is empty, unread-only
is off, no filters are active, and multi-select is not active. Otherwise the
page renders exactly the current plain list. With fewer than 3 eligible
threads the pond is hidden, since a pond of one or two bubbles looks broken.

## Who is in the pond

A new pure function `PondSelector.Select(IReadOnlyList<SmsThread> chats, int max = 8)`
in `ForgeLinkSms.Core/Utils` returns the pond threads in rank order:

1. Favorites, newest activity first.
2. Then threads with `UnreadCount > 0`, newest first.
3. Then the rest by `LastMessageTimestamp`, newest first.

It takes the first `max` of that order and skips threads without a contact name
(`DisplayName` blank), because a bubble with a raw phone number isn't
meaningful. `ConversationsViewModel` exposes `PondThreads` (the selection from
the Chats lane) and `ListThreads` (the Chats lane minus pond threads, in the
existing sort order), recomputed in `ApplyFilter()`. When the pond is hidden,
`ListThreads` is the full lane.

## Bubble look and layout

- **Photo bubble:** the contact photo cropped to a circle with a 3px colored
  rim, name centered below. Without a photo, the circle is filled with the
  person's color and shows `Initials`.
- **Person color:** picked deterministically from a fixed palette of 8 colors
  by hashing the normalized address, so a person keeps the same color
  everywhere.
- **Size by rank:** rank 1 = 88px, stepping down to rank 8 = 48px.
- **Placement:** a fixed, hand-tuned slot map (positions as percentages of the
  pond's width and height) for 3–8 bubbles, so bubbles never overlap or jump
  between loads. The pond area is 250px tall.
- **Unread:** a yellow count badge on the top-right of the bubble plus a soft
  pulsing glow.
- **Motion:** every bubble bobs 5px up and down on a 2.4–3.6s loop with
  staggered delays. All animation is disabled under
  `prefers-reduced-motion: reduce`.

## Interaction

- **Tap** opens the conversation, the same as tapping a list row.
- **Long-press (500ms)** enters multi-select with that thread selected, the same
  as a row. While selecting, the pond hides and the full plain list shows, so
  the selection toolbar and ⋮ sheet work unchanged.
- **No swipe** on bubbles; swipe-to-trash/archive stays a list-row gesture.

## Collapse on scroll

A small `bubblePond.js` (next to `infiniteScroll.js`) watches the page's
scroll position. The pond itself scrolls away normally; once it has passed
under the header, a row of 36px mini bubbles slides in (≈250ms) directly under
the header, still tappable, and it slides out again when you scroll back up.
Keeping the pond in normal flow avoids layout jumps at the threshold. It is a
CSS class toggle only; Blazor never re-renders on scroll.

## Components and files

- `ForgeLinkSms.Core/Utils/PondSelector.cs`: selection, ranking, and the
  person-color hash.
- `ForgeLinkSms.Core/ViewModels/ConversationsViewModel.cs`: `PondThreads`,
  and `ListThreads` (the pond shows when `PondThreads` is non-empty).
- `ForgeLinkSms/Components/BubblePond.razor`: renders the bubbles, raising
  `OnOpen(thread)` and `OnLongPress(thread)`.
- `ForgeLinkSms/wwwroot/js/bubblePond.js`: collapse-on-scroll.
- `ForgeLinkSms/Pages/Conversations/ConversationsPage.razor`: renders
  `BubblePond` above the list when `PondThreads` is non-empty and nothing is
  selected, and loops over
  `ListThreads` instead of `Threads` for the Chats lane.

## Testing

Unit tests for `PondSelector`: rank order (favorites, then unread, then
recent), the max-8 cap, skipping unnamed senders, and the stable person color.
View model tests for `PondThreads`/`ListThreads` (no duplicates, pond hidden
under search, unread-only, filters, non-Chats lanes, or fewer than 3 threads).
Then a check on the phone: the pond renders with real photos, tap and
long-press work, it collapses on scroll, and dark mode reads well.
