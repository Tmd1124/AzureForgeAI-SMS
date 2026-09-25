# Bubble Pond Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an animated "Bubble Pond" of contact-photo bubbles above the conversation list on the Chats tab.

**Architecture:** A pure `PondSelector` in Core picks and ranks up to 8 pond threads and gives each person a stable color. `ConversationsViewModel` exposes `PondThreads`/`ListThreads`, recomputed in `ApplyFilter()`. A new `BubblePond` Razor component draws the bubbles (plus a small sticky row of mini bubbles), and a tiny `bubblePond.js` shows that mini row once the pond scrolls out of view.

**Tech Stack:** .NET 9 MAUI Blazor Hybrid (Android), CommunityToolkit.Mvvm, xUnit + Moq, Blazor scoped CSS, plain JS.

**Spec:** `docs/superpowers/specs/2026-09-25-bubble-pond-design.md`

## Global Constraints

- The pond appears only on the Chats lane, with empty search, unread-only off, no active filters, and multi-select inactive.
- The pond holds at most 8 threads and is hidden when fewer than 3 qualify.
- Only threads with a contact name (`DisplayName` not blank) can be in the pond.
- Rank order: favorites, then unread, then newest `LastMessageTimestamp`.
- Bubble size by rank: 88, 80, 74, 68, 62, 56, 52, 48 px.
- A person's color comes from an 8-color palette via a stable hash of the normalized address.
- All animation is disabled under `prefers-reduced-motion: reduce`.
- **No git commits in any task.** The user commits only on explicit request (user's global instructions override the usual per-task commit step).

## Review Focus

1. **Color stability across launches.** `string.GetHashCode()` is randomized per process, so a person would get a new color every launch. Pinned by `ColorFor_is_stable_for_a_known_address` in Task 1.
2. **The same person in both pond and list.** Expected: never both. Pinned by `ListThreads_excludes_pond_threads` in Task 2.
3. **Trashing, archiving, or snoozing someone who is in the pond.** Expected: they leave the pond immediately and the next person moves up. Pinned by `Pond_refills_after_a_pond_thread_is_archived` in Task 2.
4. **A number formatted differently in different places** (`+1 (312) 555-0147` vs `3125550147`). Expected: same color. Pinned by `ColorFor_ignores_number_formatting` in Task 1.
5. **Scrolling that starts on a bubble.** Expected: scrolls the page and does not open the conversation or trigger a long-press. Handled by `touch-action: pan-y` plus `pointercancel`, and checked on the phone in Task 3 Step 6.

---

### Task 1: PondSelector (selection, ranking, person color)

**Files:**
- Create: `src/ForgeLinkSms.Core/Utils/PondSelector.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Utils/PondSelectorTests.cs`

**Interfaces:**
- Consumes: `SmsThread` (`DisplayName`, `IsFavorite`, `UnreadCount`, `LastMessageTimestamp`, `Address`), `PhoneNumberFormatter.ToComparableDigits(string)`.
- Produces:
  - `PondSelector.Select(IEnumerable<SmsThread> chats, int max = PondSelector.MaxBubbles) : IReadOnlyList<SmsThread>`
  - `PondSelector.ColorFor(string address) : string` (hex like `"#e11d48"`)
  - `PondSelector.MaxBubbles = 8`, `PondSelector.MinBubbles = 3`

- [ ] **Step 1: Write the failing tests**

```csharp
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class PondSelectorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static SmsThread MakeThread(long id, string? name, int minutesAgo, bool isFavorite = false, int unread = 0, string? address = null) => new()
    {
        Id = id,
        Address = address ?? $"312555{id:0000}",
        DisplayName = name,
        LastMessageBody = "hi",
        LastMessageTimestamp = Now.AddMinutes(-minutesAgo),
        UnreadCount = unread,
        IsFavorite = isFavorite
    };

    [Fact]
    public void Select_ranks_favorites_then_unread_then_most_recent()
    {
        var threads = new[]
        {
            MakeThread(1, "Recent", 1),
            MakeThread(2, "Unread", 30, unread: 2),
            MakeThread(3, "Old favorite", 500, isFavorite: true),
            MakeThread(4, "New favorite", 60, isFavorite: true),
            MakeThread(5, "Older", 90)
        };

        var pond = PondSelector.Select(threads);

        Assert.Equal(new long[] { 4, 3, 2, 1, 5 }, pond.Select(t => t.Id));
    }

    [Fact]
    public void Select_caps_the_pond_at_eight()
    {
        var threads = Enumerable.Range(1, 12).Select(i => MakeThread(i, $"P{i}", i)).ToList();

        var pond = PondSelector.Select(threads);

        Assert.Equal(8, pond.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), pond.Select(t => t.Id));
    }

    [Fact]
    public void Select_skips_threads_without_a_contact_name()
    {
        var threads = new[] { MakeThread(1, null, 1), MakeThread(2, "A", 2), MakeThread(3, "B", 3), MakeThread(4, "C", 4) };

        Assert.DoesNotContain(PondSelector.Select(threads), t => t.Id == 1);
    }

    [Fact]
    public void Select_returns_nothing_when_fewer_than_three_qualify()
    {
        var threads = new[] { MakeThread(1, "A", 1), MakeThread(2, "B", 2), MakeThread(3, null, 3) };

        Assert.Empty(PondSelector.Select(threads));
    }

    [Fact]
    public void ColorFor_is_stable_for_a_known_address()
    {
        // Pinned value: FNV-1a over "3125550147" into the 8-color palette. If this ever changes,
        // every user's people change color, so treat a failure here as a real regression.
        Assert.Equal(PondSelector.ColorFor("3125550147"), PondSelector.ColorFor("3125550147"));
        Assert.Matches("^#[0-9a-f]{6}$", PondSelector.ColorFor("3125550147"));
        Assert.Equal("#f59e0b", PondSelector.ColorFor("3125550147"));
    }

    [Fact]
    public void ColorFor_ignores_number_formatting()
    {
        Assert.Equal(PondSelector.ColorFor("3125550147"), PondSelector.ColorFor("+1 (312) 555-0147"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~PondSelectorTests"`
Expected: build error `The name 'PondSelector' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class PondSelector
{
    public const int MaxBubbles = 8;
    public const int MinBubbles = 3;

    private static readonly string[] Palette =
    {
        "#e11d48", "#7c3aed", "#0ea5e9", "#16a34a", "#f59e0b", "#db2777", "#0d9488", "#6366f1"
    };

    // A pond of one or two bubbles looks broken rather than playful, so below MinBubbles
    // there's no pond at all and the plain list shows instead.
    public static IReadOnlyList<SmsThread> Select(IEnumerable<SmsThread> chats, int max = MaxBubbles)
    {
        var picked = chats
            .Where(t => !string.IsNullOrWhiteSpace(t.DisplayName))
            .OrderByDescending(t => t.IsFavorite)
            .ThenByDescending(t => t.UnreadCount > 0)
            .ThenByDescending(t => t.LastMessageTimestamp)
            .Take(max)
            .ToList();
        return picked.Count >= MinBubbles ? picked : Array.Empty<SmsThread>();
    }

    // FNV-1a instead of string.GetHashCode(), which .NET randomizes per process: that would
    // give every person a different color each time the app launches.
    public static string ColorFor(string address)
    {
        uint hash = 2166136261;
        foreach (var c in PhoneNumberFormatter.ToComparableDigits(address))
        {
            hash ^= c;
            hash *= 16777619;
        }
        return Palette[hash % (uint)Palette.Length];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~PondSelectorTests"`
Expected: all pass.

---

### Task 2: PondThreads / ListThreads on ConversationsViewModel

**Files:**
- Modify: `src/ForgeLinkSms.Core/ViewModels/ConversationsViewModel.cs` (properties near `UpdateGroups`; end of `ApplyFilter()`)
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/ConversationsViewModelTests.cs` (append)

**Interfaces:**
- Consumes: `PondSelector.Select` (Task 1); the existing `ApplyFilter()` local `query`, plus `Lane`, `ShowUnreadOnly`, `ActiveFilterIds`, `Threads`.
- Produces:
  - `ConversationsViewModel.PondThreads : IReadOnlyList<SmsThread>` (empty when the pond shouldn't show)
  - `ConversationsViewModel.ListThreads : IReadOnlyList<SmsThread>` (`Threads` minus `PondThreads`, same order)

- [ ] **Step 1: Write the failing tests** (append inside `ConversationsViewModelTests`; `MakeThread`, `MakeViewModel`, `MakeEmptyFavoriteRepository` already exist in that file)

```csharp
    private static List<SmsThread> FiveNamedChats() => new()
    {
        MakeThread(1, "5550000001", "Ana", "a", DateTimeOffset.UtcNow.AddMinutes(-1)),
        MakeThread(2, "5550000002", "Ben", "b", DateTimeOffset.UtcNow.AddMinutes(-2)),
        MakeThread(3, "5550000003", "Cal", "c", DateTimeOffset.UtcNow.AddMinutes(-3)),
        MakeThread(4, "5550000004", "Dee", "d", DateTimeOffset.UtcNow.AddMinutes(-4)),
        MakeThread(5, "5550000005", null, "e", DateTimeOffset.UtcNow.AddMinutes(-5))
    };

    [Fact]
    public async Task PondThreads_holds_named_chats_in_rank_order()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var favorites = MakeEmptyFavoriteRepository();
        favorites.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 4 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favorites);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 4, 1, 2, 3 }, viewModel.PondThreads.Select(t => t.Id));
    }

    [Fact]
    public async Task ListThreads_excludes_pond_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 5 }, viewModel.ListThreads.Select(t => t.Id));
        Assert.Empty(viewModel.PondThreads.Select(t => t.Id).Intersect(viewModel.ListThreads.Select(t => t.Id)));
    }

    [Fact]
    public async Task Pond_is_hidden_while_searching_filtering_or_off_the_chats_lane()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "a";
        Assert.Empty(viewModel.PondThreads);
        Assert.Equal(viewModel.Threads.Select(t => t.Id), viewModel.ListThreads.Select(t => t.Id));
        viewModel.SearchText = string.Empty;

        viewModel.ShowUnreadOnly = true;
        Assert.Empty(viewModel.PondThreads);
        viewModel.ShowUnreadOnly = false;

        viewModel.Lane = ConversationLane.Updates;
        Assert.Empty(viewModel.PondThreads);
        viewModel.Lane = ConversationLane.Conversations;

        Assert.NotEmpty(viewModel.PondThreads);
    }

    [Fact]
    public async Task Pond_refills_after_a_pond_thread_is_archived()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(FiveNamedChats());
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ArchiveThreadCommand.ExecuteAsync(1L);

        Assert.Equal(new long[] { 2, 3, 4 }, viewModel.PondThreads.Select(t => t.Id));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~ConversationsViewModelTests"`
Expected: build error `'ConversationsViewModel' does not contain a definition for 'PondThreads'`.

- [ ] **Step 3: Implement**

Add next to the existing `UpdateGroups` property:

```csharp
    public IReadOnlyList<SmsThread> PondThreads { get; private set; } = Array.Empty<SmsThread>();

    public IReadOnlyList<SmsThread> ListThreads { get; private set; } = Array.Empty<SmsThread>();
```

At the very end of `ApplyFilter()` (after the existing `OnPropertyChanged(nameof(UpdatesCount));`):

```csharp
        var showPond = Lane == ConversationLane.Conversations && string.IsNullOrEmpty(query)
            && !ShowUnreadOnly && ActiveFilterIds.Count == 0;
        PondThreads = showPond ? PondSelector.Select(Threads) : Array.Empty<SmsThread>();
        var pondIds = PondThreads.Select(t => t.Id).ToHashSet();
        ListThreads = Threads.Where(t => !pondIds.Contains(t.Id)).ToList();
        OnPropertyChanged(nameof(PondThreads));
        OnPropertyChanged(nameof(ListThreads));
```

(`query` is the existing `var query = SearchText.Trim();` at the top of `ApplyFilter()`.)

- [ ] **Step 4: Run all Core tests**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests`
Expected: all pass (previous 302 plus the new ones).

---

### Task 3: BubblePond component wired into the Chats tab

**Files:**
- Create: `src/ForgeLinkSms/Components/BubblePond.razor`
- Create: `src/ForgeLinkSms/Components/BubblePond.razor.css`
- Modify: `src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor`: header `id` (line ~35), pond render (just before `@if (_isLoading)`), `VisibleRows()` row source (line ~597), a new `SelectFromPond` method

**Interfaces:**
- Consumes: `ConversationsViewModel.PondThreads` / `ListThreads` (Task 2), `PondSelector.ColorFor` (Task 1), `SmsThread.PhotoUri` / `Initials` / `DisplayNameOrAddress` / `UnreadCount`, the page's existing `OpenThread(long threadId, string address)` and `_selectedThreadIds`.
- Produces:
  - `<BubblePond Threads="IReadOnlyList<SmsThread>" OnOpen="EventCallback<SmsThread>" OnLongPress="EventCallback<SmsThread>" />`
  - DOM ids used by Task 4: `bubble-pond` (the pond), `bubble-pond-mini` (the mini row), `conversations-header` (the page header)

- [ ] **Step 1: Create `Components/BubblePond.razor`**

```razor
@using ForgeLinkSms.Core.Models
@using ForgeLinkSms.Core.Utils

<div id="bubble-pond" class="bubble-pond">
    @for (var i = 0; i < Threads.Count; i++)
    {
        var thread = Threads[i];
        var (xPercent, y) = Slots[i];
        var size = Sizes[i];
        var color = PondSelector.ColorFor(thread.Address);
        <div @key="thread.Id"
             class="pond-bubble @(thread.UnreadCount > 0 ? "unread" : "")"
             style="left:calc(@(xPercent)% - @(size / 2)px);top:@(y - size / 2)px;width:@(size)px;height:@(size)px;--rim:@color;animation-delay:@((i * 0.37).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))s;"
             @onpointerdown="() => StartPress(thread)"
             @onpointerup="() => EndPress(thread)"
             @onpointerleave="CancelPress"
             @onpointercancel="CancelPress">
            @if (!string.IsNullOrEmpty(thread.PhotoUri))
            {
                <img src="@thread.PhotoUri" alt="" draggable="false" />
            }
            else
            {
                <span class="initials" style="background:@color">@thread.Initials</span>
            }
            @if (thread.UnreadCount > 0)
            {
                <span class="count">@thread.UnreadCount</span>
            }
            <span class="name">@FirstName(thread)</span>
        </div>
    }
</div>

<div id="bubble-pond-mini" class="bubble-pond-mini">
    @foreach (var thread in Threads)
    {
        var color = PondSelector.ColorFor(thread.Address);
        <div @key="thread.Id" class="mini-bubble @(thread.UnreadCount > 0 ? "unread" : "")" style="--rim:@color"
             @onclick="() => OnOpen.InvokeAsync(thread)">
            @if (!string.IsNullOrEmpty(thread.PhotoUri))
            {
                <img src="@thread.PhotoUri" alt="@thread.DisplayNameOrAddress" draggable="false" />
            }
            else
            {
                <span class="initials" style="background:@color">@thread.Initials</span>
            }
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<SmsThread> Threads { get; set; } = Array.Empty<SmsThread>();
    [Parameter] public EventCallback<SmsThread> OnOpen { get; set; }
    [Parameter] public EventCallback<SmsThread> OnLongPress { get; set; }

    // Hand-tuned bubble centers (x as % of pond width, y in px) for rank order, spaced so
    // the rank sizes below never overlap, even with names underneath. Fewer than 8 bubbles
    // use the first N slots.
    private static readonly (double XPercent, int Y)[] Slots =
    {
        (22, 60), (58, 42), (84, 96), (44, 134), (14, 168), (70, 178), (40, 206), (92, 196)
    };

    private static readonly int[] Sizes = { 88, 80, 74, 68, 62, 56, 52, 48 };

    private CancellationTokenSource? _pressCts;
    private long? _pressedThreadId;
    private bool _longPressed;

    private void StartPress(SmsThread thread)
    {
        _pressCts?.Cancel();
        _pressedThreadId = thread.Id;
        _longPressed = false;
        _pressCts = new CancellationTokenSource();
        var token = _pressCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                _longPressed = true;
                await InvokeAsync(() => OnLongPress.InvokeAsync(thread));
            }
            catch (TaskCanceledException)
            {
                // released, scrolled, or left the bubble before the long-press threshold
            }
        });
    }

    private async Task EndPress(SmsThread thread)
    {
        _pressCts?.Cancel();
        // A pointerup without our own pointerdown (e.g. an overlay closing mid-gesture) isn't a tap.
        if (_pressedThreadId != thread.Id || _longPressed)
        {
            _pressedThreadId = null;
            return;
        }
        _pressedThreadId = null;
        await OnOpen.InvokeAsync(thread);
    }

    private void CancelPress()
    {
        _pressCts?.Cancel();
        _pressedThreadId = null;
    }

    private static string FirstName(SmsThread thread) =>
        thread.DisplayNameOrAddress.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? thread.DisplayNameOrAddress;
}
```

- [ ] **Step 2: Create `Components/BubblePond.razor.css`**

```css
.bubble-pond {
    position: relative;
    height: 250px;
    margin: 4px 0 8px;
}

.pond-bubble {
    position: absolute;
    box-sizing: border-box;
    border-radius: 50%;
    border: 3px solid var(--rim);
    cursor: pointer;
    touch-action: pan-y;
    user-select: none;
    -webkit-user-select: none;
    -webkit-touch-callout: none;
    animation: pond-bob 3s ease-in-out infinite;
}

.pond-bubble.unread {
    animation: pond-bob 2.4s ease-in-out infinite, pond-pulse 1.8s infinite;
}

.pond-bubble img,
.pond-bubble .initials,
.mini-bubble img,
.mini-bubble .initials {
    width: 100%;
    height: 100%;
    border-radius: 50%;
    object-fit: cover;
    display: flex;
    align-items: center;
    justify-content: center;
    color: white;
    font-weight: 700;
    pointer-events: none;
}

.count {
    position: absolute;
    top: -4px;
    right: -4px;
    min-width: 20px;
    height: 20px;
    padding: 0 5px;
    box-sizing: border-box;
    border-radius: 10px;
    background: #facc15;
    color: #111;
    font-size: 0.72em;
    font-weight: 800;
    display: flex;
    align-items: center;
    justify-content: center;
}

.name {
    position: absolute;
    left: 50%;
    top: 100%;
    transform: translateX(-50%);
    margin-top: 3px;
    max-width: 90px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.72em;
    font-weight: 600;
    color: var(--text-color);
}

.bubble-pond-mini {
    position: fixed;
    left: 0;
    right: 0;
    top: 52px;
    z-index: 9;
    display: flex;
    gap: 8px;
    padding: 6px 12px;
    overflow-x: auto;
    background: var(--surface-color);
    box-shadow: 0 4px 8px rgba(0, 0, 0, 0.15);
    opacity: 0;
    transform: translateY(-10px);
    pointer-events: none;
    transition: opacity 0.25s, transform 0.25s;
}

.bubble-pond-mini.show {
    opacity: 1;
    transform: none;
    pointer-events: auto;
}

.mini-bubble {
    position: relative;
    flex-shrink: 0;
    width: 36px;
    height: 36px;
    box-sizing: border-box;
    border-radius: 50%;
    border: 2px solid var(--rim);
    cursor: pointer;
}

.mini-bubble.unread::after {
    content: "";
    position: absolute;
    top: -2px;
    right: -2px;
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background: #facc15;
}

@keyframes pond-bob {
    0%, 100% { transform: translateY(0); }
    50% { transform: translateY(-5px); }
}

@keyframes pond-pulse {
    0%, 100% { box-shadow: 0 0 0 0 rgba(250, 204, 21, 0.7); }
    70% { box-shadow: 0 0 0 10px rgba(250, 204, 21, 0); }
}

@media (prefers-reduced-motion: reduce) {
    .pond-bubble,
    .pond-bubble.unread {
        animation: none;
    }

    .bubble-pond-mini {
        transition: none;
    }
}
```

- [ ] **Step 3: Wire it into `ConversationsPage.razor`**

3a. Give the normal (non-selection) header an id. Change the opening tag at line ~35 from
`<div style="position:sticky;top:0;z-index:10;background:var(--surface-color);display:flex;justify-content:space-between;...">`
to the same tag with `id="conversations-header"` added as its first attribute.

3b. Immediately before the `@if (_isLoading)` block, add:

```razor
    @if (_selectedThreadIds.Count == 0 && ViewModel.PondThreads.Count > 0)
    {
        <BubblePond Threads="ViewModel.PondThreads"
                    OnOpen="t => OpenThread(t.Id, t.Address)"
                    OnLongPress="SelectFromPond" />
    }
```

3c. In `VisibleRows()`, replace the non-Updates branch's `foreach (var thread in ViewModel.Threads)` with:

```csharp
            // While selecting, the pond hides, so every thread (pond ones included) must be in the list.
            var rows = _selectedThreadIds.Count == 0 && ViewModel.PondThreads.Count > 0 ? ViewModel.ListThreads : ViewModel.Threads;
            foreach (var thread in rows)
```

3d. Add next to `CloseActionSheet()`:

```csharp
    private void SelectFromPond(SmsThread thread) => _selectedThreadIds.Add(thread.Id);
```

- [ ] **Step 4: Run tests and build the APK**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests` (from the repo root). Expected: all pass.
Run (from `src/ForgeLinkSms`): `dotnet build -f net9.0-android -t:SignAndroidPackage -p:EmbedAssembliesIntoApk=true`
Expected: `Build succeeded.`

- [ ] **Step 5: Install and launch on the phone**

```bash
ADB="/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"
"$ADB" install -r bin/Debug/net9.0-android/com.companyname.forgelinksms-Signed.apk
"$ADB" shell am force-stop com.companyname.forgelinksms
"$ADB" shell monkey -p com.companyname.forgelinksms -c android.intent.category.LAUNCHER 1
```

- [ ] **Step 6: Verify on the phone** (screenshot with a unique file name only: `screencap -d 4630946449689556883 -p /sdcard/claude_check.png`, pull it, then delete **only** `/sdcard/claude_check.png`)

Check all of these: the pond shows 8 photo bubbles above the list on Chats; bubbles don't overlap and names are readable; unread bubbles pulse with a yellow count; tapping a bubble opens that conversation; long-pressing a bubble enters select mode, and the pond disappears while all rows (including pond people) show in the list; a vertical swipe that starts on a bubble scrolls the page without opening anything; the Updates and Screener tabs and search show no pond; dark mode text is readable.

---

### Task 4: Mini-row on scroll (bubblePond.js)

**Files:**
- Create: `src/ForgeLinkSms/wwwroot/js/bubblePond.js`
- Modify: `src/ForgeLinkSms/wwwroot/index.html` (add a script tag after `js/infiniteScroll.js`)
- Modify: `src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor`: inject `IJSRuntime`, call observe on first render, dispose in `Dispose()`

**Interfaces:**
- Consumes: DOM ids `bubble-pond`, `bubble-pond-mini`, `conversations-header` (Task 3).
- Produces: `window.forgeLinkBubblePond.observe(pondId, miniId, headerId)` and `window.forgeLinkBubblePond.dispose()`.

- [ ] **Step 1: Create `wwwroot/js/bubblePond.js`**

```js
// Listens on document with capture so it catches the scroll wherever the WebView puts it
// (window or an inner container). Elements are looked up on every update because Blazor adds
// and removes the pond (search, select mode, lane changes) after observe() runs.
window.forgeLinkBubblePond = {
    handler: null,
    observe: function (pondId, miniId, headerId) {
        this.dispose();
        var ticking = false;
        var update = function () {
            ticking = false;
            var pond = document.getElementById(pondId);
            var mini = document.getElementById(miniId);
            if (!pond || !mini) {
                return;
            }
            var header = document.getElementById(headerId);
            var headerBottom = header ? header.getBoundingClientRect().bottom : 0;
            mini.style.top = headerBottom + "px";
            mini.classList.toggle("show", pond.getBoundingClientRect().bottom < headerBottom + 40);
        };
        this.handler = function () {
            if (!ticking) {
                ticking = true;
                requestAnimationFrame(update);
            }
        };
        document.addEventListener("scroll", this.handler, { passive: true, capture: true });
        update();
    },
    dispose: function () {
        if (this.handler) {
            document.removeEventListener("scroll", this.handler, { capture: true });
            this.handler = null;
        }
    }
};
```

- [ ] **Step 2: Load it in `wwwroot/index.html`**

After `<script src="js/infiniteScroll.js"></script>` add:

```html
    <script src="js/bubblePond.js"></script>
```

- [ ] **Step 3: Observe from `ConversationsPage.razor`**

Add with the other `@inject` lines:

```razor
@inject Microsoft.JSInterop.IJSRuntime JS
```

At the top of the existing `OnAfterRenderAsync(bool firstRender)`:

```csharp
        if (firstRender)
        {
            await JS.InvokeVoidAsync("forgeLinkBubblePond.observe", "bubble-pond", "bubble-pond-mini", "conversations-header");
        }
```

In the existing `Dispose()`, add:

```csharp
        // Fire-and-forget: the WebView may already be tearing down, and a leftover listener only
        // looks up elements that no longer exist.
        _ = JS.InvokeVoidAsync("forgeLinkBubblePond.dispose").AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
```

- [ ] **Step 4: Build, install, verify on the phone**

Build and install exactly as in Task 3 Steps 4–5. Then check on the phone: scrolling down until the pond passes under the header slides in a row of small bubbles directly under the header; tapping a mini bubble opens that conversation; scrolling back to the top hides the row; the row never covers the header buttons; with the phone's "Remove animations" setting on, the row appears without sliding and the bubbles don't bob.

- [ ] **Step 5: Final full test run**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests`
Expected: all pass.
