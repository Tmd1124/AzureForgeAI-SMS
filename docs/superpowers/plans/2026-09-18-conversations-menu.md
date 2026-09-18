# Conversations Menu, Theme, Profile, Trash, Blocked, and Undo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the app's bottom Messages/Settings nav bar with a Google-Messages-style top-right menu on Conversations, and add the six destinations behind it: Message Settings (existing), Theme, Your Profile, Trash, Blocked, Mark as Read, and Undo.

**Architecture:** Two new SQLite tables (`TrashedThread`, `BlockedNumber`, via `sqlite-net-pcl`) live directly in `SmsMessenger.Core` since sqlite-net is portable .NET with no Android dependency — the first genuinely persisted, queryable app state this project has needed, alongside Android's SMS provider which remains the source of truth for message content. Everything else new (theme mode/accent, profile name/photo path) is a single scalar and stays in `Preferences`, matching the existing pattern. An in-memory `IUndoStack` in Core records the last few destructive actions (trash, block, mark-as-read) as small `IUndoableAction` objects. Blocking a number additionally writes to Android's `BlockedNumberContract` so the OS itself stops delivering future texts.

**Tech Stack:** .NET 9, .NET MAUI (Blazor Hybrid), CommunityToolkit.Mvvm, sqlite-net-pcl, xUnit + Moq, Android SDK, git.

**Spec:** `docs/superpowers/specs/2026-09-18-conversations-menu-design.md` — this plan implements that design exactly as scoped there.

## Global Constraints

- Continues every constraint from the original plan (`docs/superpowers/plans/2026-09-17-sms-messenger.md`): Android only, no backend/cloud/push, Android's SMS provider stays the source of truth for message content.
- Trash and Blocked operate on whole threads, never individual messages within a thread.
- Trashing a thread never deletes anything from Android's SMS provider — it only records the thread ID locally; the original messages are always recoverable.
- The undo stack is in-memory only and resets when the app restarts (confirmed with the user during design).
- Undo covers exactly three action types: trashing a thread, blocking a number, and the global mark-all-as-read. Sending/receiving messages is never undoable.
- No new abstraction is introduced for the existing single-value Preferences entries — theme mode/accent and profile name/photo path are read/written directly via `Microsoft.Maui.Storage.Preferences` inside each feature's Android service, exactly like the original plan never introduced an `IPreferencesService` wrapper either.
- Every new Core interface gets exactly one Android implementation in `Platforms/Android/`, following the codebase's existing one-interface-per-concern convention (`IContactService`, `IThreadService`, `ISmsService`, etc.) — except the two SQLite repositories, which are portable and implemented directly in `SmsMessenger.Core/Data/`.

---

### Task 1: SQLite data layer for Trash and Blocked numbers

**Files:**
- Modify: `src/SmsMessenger.Core/SmsMessenger.Core.csproj` (add `sqlite-net-pcl` package)
- Create: `src/SmsMessenger.Core/Models/TrashedThread.cs`
- Create: `src/SmsMessenger.Core/Models/BlockedNumber.cs`
- Create: `src/SmsMessenger.Core/Data/ITrashRepository.cs`
- Create: `src/SmsMessenger.Core/Data/TrashRepository.cs`
- Create: `src/SmsMessenger.Core/Data/IBlockedNumberRepository.cs`
- Create: `src/SmsMessenger.Core/Data/BlockedNumberRepository.cs`
- Test: `tests/SmsMessenger.Core.Tests/Data/TrashRepositoryTests.cs`
- Test: `tests/SmsMessenger.Core.Tests/Data/BlockedNumberRepositoryTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `ITrashRepository` (`InitializeAsync()`, `TrashThreadAsync(long)`, `RestoreThreadAsync(long)`, `IsTrashedAsync(long)`, `GetTrashedThreadIdsAsync()`) and `IBlockedNumberRepository` (`InitializeAsync()`, `BlockAsync(string)`, `UnblockAsync(string)`, `IsBlockedAsync(string)`, `GetBlockedNumbersAsync()` returning `IReadOnlyList<BlockedNumber>`) — every later task in this plan that touches Trash or Blocked builds on these exact signatures. Both implementations take a raw `string databasePath` constructor parameter so tests can point them at a temp file and the app can point them at `FileSystem.AppDataDirectory` without `SmsMessenger.Core` ever referencing a MAUI-specific API.

- [ ] **Step 1: Add the sqlite-net-pcl package**

```bash
dotnet add src/SmsMessenger.Core/SmsMessenger.Core.csproj package sqlite-net-pcl
```

- [ ] **Step 2: Write the failing tests for `TrashRepository`**

`tests/SmsMessenger.Core.Tests/Data/TrashRepositoryTests.cs`:

```csharp
using SmsMessenger.Core.Data;

namespace SmsMessenger.Core.Tests.Data;

public class TrashRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TrashRepository _repository;

    public TrashRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trash-test-{Guid.NewGuid()}.db3");
        _repository = new TrashRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task GetTrashedThreadIdsAsync_is_empty_initially()
    {
        var ids = await _repository.GetTrashedThreadIdsAsync();

        Assert.Empty(ids);
    }

    [Fact]
    public async Task TrashThreadAsync_then_GetTrashedThreadIdsAsync_returns_it()
    {
        await _repository.TrashThreadAsync(42);

        var ids = await _repository.GetTrashedThreadIdsAsync();

        Assert.Equal(new long[] { 42 }, ids);
    }

    [Fact]
    public async Task IsTrashedAsync_reflects_trashed_state()
    {
        Assert.False(await _repository.IsTrashedAsync(7));

        await _repository.TrashThreadAsync(7);

        Assert.True(await _repository.IsTrashedAsync(7));
    }

    [Fact]
    public async Task RestoreThreadAsync_removes_it_from_the_trashed_list()
    {
        await _repository.TrashThreadAsync(42);

        await _repository.RestoreThreadAsync(42);

        Assert.Empty(await _repository.GetTrashedThreadIdsAsync());
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter TrashRepositoryTests
```

Expected: FAIL — `TrashRepository` does not exist yet.

- [ ] **Step 4: Implement `TrashedThread`, `ITrashRepository`, and `TrashRepository`**

`src/SmsMessenger.Core/Models/TrashedThread.cs`:

```csharp
using SQLite;

namespace SmsMessenger.Core.Models;

public class TrashedThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public DateTimeOffset TrashedAtUtc { get; set; }
}
```

`src/SmsMessenger.Core/Data/ITrashRepository.cs`:

```csharp
namespace SmsMessenger.Core.Data;

public interface ITrashRepository
{
    Task InitializeAsync();
    Task TrashThreadAsync(long threadId);
    Task RestoreThreadAsync(long threadId);
    Task<bool> IsTrashedAsync(long threadId);
    Task<IReadOnlyList<long>> GetTrashedThreadIdsAsync();
}
```

`src/SmsMessenger.Core/Data/TrashRepository.cs`:

```csharp
using SQLite;
using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Data;

public class TrashRepository : ITrashRepository
{
    private readonly SQLiteAsyncConnection _db;

    public TrashRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<TrashedThread>();

    public Task TrashThreadAsync(long threadId) =>
        _db.InsertOrReplaceAsync(new TrashedThread { ThreadId = threadId, TrashedAtUtc = DateTimeOffset.UtcNow });

    public Task RestoreThreadAsync(long threadId) => _db.DeleteAsync<TrashedThread>(threadId);

    public async Task<bool> IsTrashedAsync(long threadId) =>
        await _db.FindAsync<TrashedThread>(threadId) is not null;

    public async Task<IReadOnlyList<long>> GetTrashedThreadIdsAsync()
    {
        var rows = await _db.Table<TrashedThread>().ToListAsync();
        return rows.Select(r => r.ThreadId).ToList();
    }
}
```

- [ ] **Step 5: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter TrashRepositoryTests
```

Expected: PASS (4 tests).

- [ ] **Step 6: Write the failing tests for `BlockedNumberRepository`**

`tests/SmsMessenger.Core.Tests/Data/BlockedNumberRepositoryTests.cs`:

```csharp
using SmsMessenger.Core.Data;

namespace SmsMessenger.Core.Tests.Data;

public class BlockedNumberRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly BlockedNumberRepository _repository;

    public BlockedNumberRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"blocked-test-{Guid.NewGuid()}.db3");
        _repository = new BlockedNumberRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task GetBlockedNumbersAsync_is_empty_initially()
    {
        var numbers = await _repository.GetBlockedNumbersAsync();

        Assert.Empty(numbers);
    }

    [Fact]
    public async Task BlockAsync_then_GetBlockedNumbersAsync_returns_it()
    {
        await _repository.BlockAsync("5550142231");

        var numbers = await _repository.GetBlockedNumbersAsync();

        Assert.Single(numbers);
        Assert.Equal("5550142231", numbers[0].PhoneNumber);
    }

    [Fact]
    public async Task IsBlockedAsync_reflects_blocked_state()
    {
        Assert.False(await _repository.IsBlockedAsync("5550142231"));

        await _repository.BlockAsync("5550142231");

        Assert.True(await _repository.IsBlockedAsync("5550142231"));
    }

    [Fact]
    public async Task UnblockAsync_removes_it_from_the_blocked_list()
    {
        await _repository.BlockAsync("5550142231");

        await _repository.UnblockAsync("5550142231");

        Assert.Empty(await _repository.GetBlockedNumbersAsync());
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter BlockedNumberRepositoryTests
```

Expected: FAIL — `BlockedNumberRepository` does not exist yet.

- [ ] **Step 8: Implement `BlockedNumber`, `IBlockedNumberRepository`, and `BlockedNumberRepository`**

`src/SmsMessenger.Core/Models/BlockedNumber.cs`:

```csharp
using SQLite;

namespace SmsMessenger.Core.Models;

public class BlockedNumber
{
    [PrimaryKey]
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset BlockedAtUtc { get; set; }
}
```

`src/SmsMessenger.Core/Data/IBlockedNumberRepository.cs`:

```csharp
using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Data;

public interface IBlockedNumberRepository
{
    Task InitializeAsync();
    Task BlockAsync(string phoneNumber);
    Task UnblockAsync(string phoneNumber);
    Task<bool> IsBlockedAsync(string phoneNumber);
    Task<IReadOnlyList<BlockedNumber>> GetBlockedNumbersAsync();
}
```

`src/SmsMessenger.Core/Data/BlockedNumberRepository.cs`:

```csharp
using SQLite;
using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Data;

public class BlockedNumberRepository : IBlockedNumberRepository
{
    private readonly SQLiteAsyncConnection _db;

    public BlockedNumberRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<BlockedNumber>();

    public Task BlockAsync(string phoneNumber) =>
        _db.InsertOrReplaceAsync(new BlockedNumber { PhoneNumber = phoneNumber, BlockedAtUtc = DateTimeOffset.UtcNow });

    public Task UnblockAsync(string phoneNumber) => _db.DeleteAsync<BlockedNumber>(phoneNumber);

    public async Task<bool> IsBlockedAsync(string phoneNumber) =>
        await _db.FindAsync<BlockedNumber>(phoneNumber) is not null;

    public async Task<IReadOnlyList<BlockedNumber>> GetBlockedNumbersAsync()
    {
        var rows = await _db.Table<BlockedNumber>().ToListAsync();
        return rows;
    }
}
```

- [ ] **Step 9: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter BlockedNumberRepositoryTests
```

Expected: PASS (4 tests).

- [ ] **Step 10: Run the full suite to confirm nothing else broke**

```bash
dotnet test tests/SmsMessenger.Core.Tests
```

Expected: PASS, all tests (36 from the original plan + 8 new).

- [ ] **Step 11: Commit**

```bash
git add src/SmsMessenger.Core/SmsMessenger.Core.csproj src/SmsMessenger.Core/Models/TrashedThread.cs src/SmsMessenger.Core/Models/BlockedNumber.cs src/SmsMessenger.Core/Data tests/SmsMessenger.Core.Tests/Data
git commit -m "feat: add SQLite-backed Trash and Blocked number repositories"
```

---

### Task 2: Undo stack

**Files:**
- Create: `src/SmsMessenger.Core/Services/IUndoableAction.cs`
- Create: `src/SmsMessenger.Core/Services/IUndoStack.cs`
- Create: `src/SmsMessenger.Core/Services/UndoStack.cs`
- Test: `tests/SmsMessenger.Core.Tests/Services/UndoStackTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `IUndoableAction` (`Description: string`, `UndoAsync(): Task`) and `IUndoStack` (`HasActions: bool`, `Push(IUndoableAction)`, `UndoAsync(): Task<string?>` — returns the popped action's `Description`, or `null` if the stack was empty). Every concrete undo action in Tasks 4/5/6 implements `IUndoableAction`; `MenuViewModel` (Task 6) is the only consumer of `IUndoStack.UndoAsync()`.

- [ ] **Step 1: Write the failing tests**

`tests/SmsMessenger.Core.Tests/Services/UndoStackTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.Tests.Services;

public class UndoStackTests
{
    [Fact]
    public void HasActions_is_false_when_empty()
    {
        var stack = new UndoStack();

        Assert.False(stack.HasActions);
    }

    [Fact]
    public async Task UndoAsync_returns_null_when_empty()
    {
        var stack = new UndoStack();

        var result = await stack.UndoAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task UndoAsync_calls_UndoAsync_on_the_most_recently_pushed_action()
    {
        var stack = new UndoStack();
        var first = new Mock<IUndoableAction>();
        first.Setup(a => a.Description).Returns("first");
        var second = new Mock<IUndoableAction>();
        second.Setup(a => a.Description).Returns("second");
        stack.Push(first.Object);
        stack.Push(second.Object);

        var result = await stack.UndoAsync();

        Assert.Equal("second", result);
        second.Verify(a => a.UndoAsync(), Times.Once);
        first.Verify(a => a.UndoAsync(), Times.Never);
    }

    [Fact]
    public async Task UndoAsync_pops_actions_in_LIFO_order_across_multiple_calls()
    {
        var stack = new UndoStack();
        var first = new Mock<IUndoableAction>();
        first.Setup(a => a.Description).Returns("first");
        var second = new Mock<IUndoableAction>();
        second.Setup(a => a.Description).Returns("second");
        stack.Push(first.Object);
        stack.Push(second.Object);

        await stack.UndoAsync();
        var result = await stack.UndoAsync();

        Assert.Equal("first", result);
        Assert.False(stack.HasActions);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter UndoStackTests
```

Expected: FAIL — `UndoStack` does not exist yet.

- [ ] **Step 3: Implement `IUndoableAction`, `IUndoStack`, and `UndoStack`**

`src/SmsMessenger.Core/Services/IUndoableAction.cs`:

```csharp
namespace SmsMessenger.Core.Services;

public interface IUndoableAction
{
    string Description { get; }
    Task UndoAsync();
}
```

`src/SmsMessenger.Core/Services/IUndoStack.cs`:

```csharp
namespace SmsMessenger.Core.Services;

public interface IUndoStack
{
    bool HasActions { get; }
    void Push(IUndoableAction action);
    Task<string?> UndoAsync();
}
```

`src/SmsMessenger.Core/Services/UndoStack.cs`:

```csharp
namespace SmsMessenger.Core.Services;

public class UndoStack : IUndoStack
{
    private readonly Stack<IUndoableAction> _actions = new();

    public bool HasActions => _actions.Count > 0;

    public void Push(IUndoableAction action) => _actions.Push(action);

    public async Task<string?> UndoAsync()
    {
        if (_actions.Count == 0)
        {
            return null;
        }

        var action = _actions.Pop();
        await action.UndoAsync();
        return action.Description;
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter UndoStackTests
```

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SmsMessenger.Core/Services/IUndoableAction.cs src/SmsMessenger.Core/Services/IUndoStack.cs src/SmsMessenger.Core/Services/UndoStack.cs tests/SmsMessenger.Core.Tests/Services/UndoStackTests.cs
git commit -m "feat: add in-memory undo stack"
```

---

### Task 3: Navigation shell — top-right menu replaces the bottom nav

**Files:**
- Delete: `src/SmsMessenger/Components/Layout/BottomNav.razor`
- Modify: `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor` (remove `<BottomNav />`, add a top-right gear icon, move the FAB back down now the bottom nav is gone)
- Modify: `src/SmsMessenger/Pages/Settings/SettingsPage.razor` (remove `<BottomNav />`)
- Create: `src/SmsMessenger/Pages/Menu/MenuPage.razor`

**Interfaces:**
- Consumes: nothing from earlier tasks besides routing conventions already established (`NavigationManager.NavigateTo`).
- Produces: the `/menu` route with a single working row ("Message Settings"). Tasks 4–7 each add exactly one more row to this same file as their feature lands — by Task 7 it has every row the spec calls for. The Profile photo/name header the spec describes is added in Task 8, once `IProfileService` exists; until then the header is just a plain "Menu" title.

- [ ] **Step 1: Delete `BottomNav.razor` and its usages**

```bash
rm src/SmsMessenger/Components/Layout/BottomNav.razor
```

Remove the `<BottomNav />` line from `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor` and from `src/SmsMessenger/Pages/Settings/SettingsPage.razor` (both currently have it as the last line before their `@code` block, added in the original plan's Task 12).

- [ ] **Step 2: Add the top-right menu icon to Conversations, and drop the FAB back down**

Replace the full contents of `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor` with:

```razor
@page "/conversations"
@using Microsoft.AspNetCore.Components
@inject SmsMessenger.Core.ViewModels.ConversationsViewModel ViewModel
@inject NavigationManager Nav

<div style="padding:12px;">
    <div style="display:flex;justify-content:flex-end;margin-bottom:8px;">
        <button @onclick="@(() => Nav.NavigateTo("/menu"))"
                style="width:40px;height:40px;border-radius:50%;border:none;background:#e2e8f0;font-size:1.2em;">
            ⚙
        </button>
    </div>

    <input placeholder="Search contacts or numbers" @bind="ViewModel.SearchText" @bind:event="oninput" />

    @if (ViewModel.Threads.Count == 0)
    {
        <p>No conversations yet.</p>
    }
    else
    {
        @foreach (var thread in ViewModel.Threads)
        {
            <div @onclick="() => OpenThread(thread.Id, thread.Address)" style="padding:10px;border-bottom:1px solid #eee;">
                <strong>@thread.DisplayNameOrAddress</strong>
                @if (thread.UnreadCount > 0)
                {
                    <span style="color:#06b6d4;"> ● @thread.UnreadCount</span>
                }
                <div style="color:#94a3b8;font-size:0.85em;">@thread.PreviewText</div>
            </div>
        }
    }

    <button @onclick="@(() => Nav.NavigateTo("/compose"))"
            style="position:fixed;right:24px;bottom:24px;width:56px;height:56px;border-radius:50%;background:#25D366;color:white;font-size:1.5em;border:none;">
        +
    </button>
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OpenThread(long threadId, string address)
    {
        Nav.NavigateTo($"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}");
    }
}
```

(The FAB's `bottom` moved from `76px` back to `24px` — that offset only existed to clear the bottom nav bar this task removes. The long-press action sheet from Task 4 adds more to this file; this step's version is deliberately the simplest correct state before that lands.)

- [ ] **Step 3: Remove `<BottomNav />` from Settings**

`src/SmsMessenger/Pages/Settings/SettingsPage.razor` becomes:

```razor
@page "/settings"
@inject SmsMessenger.Core.ViewModels.SettingsViewModel ViewModel

<div style="padding:12px;">
    <h3>Settings</h3>
    <p>Default SMS app: @(ViewModel.IsDefaultSmsApp ? "Yes ✓" : "No — open Onboarding to fix this")</p>
    <label>
        <input type="checkbox" @bind="ViewModel.NotificationsEnabled" />
        Notify me about incoming texts
    </label>
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }
}
```

- [ ] **Step 4: Create the Menu page**

`src/SmsMessenger/Pages/Menu/MenuPage.razor`:

```razor
@page "/menu"
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    <h3>Menu</h3>

    <div @onclick="@(() => Nav.NavigateTo("/settings"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        ⚙ Message Settings
    </div>
</div>
```

- [ ] **Step 5: Build to verify it compiles**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 6: Manual verification on the physical device**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -t:Run -f net9.0-android
```

On the device: open Conversations, confirm the bottom Messages/Settings bar is gone and a gear icon appears top-right. Tap it — confirm `/menu` opens with a "Message Settings" row. Tap that row — confirm it opens the real Settings screen (with no bottom nav there either). Confirm the Compose FAB still sits correctly at the bottom-right with nothing overlapping it.

- [ ] **Step 7: Commit**

```bash
git add src/SmsMessenger/Components/Layout/BottomNav.razor src/SmsMessenger/Pages/Conversations/ConversationsPage.razor src/SmsMessenger/Pages/Settings/SettingsPage.razor src/SmsMessenger/Pages/Menu
git commit -m "feat: replace bottom nav with a top-right menu"
```

---

### Task 4: Trash feature

**Files:**
- Modify: `src/SmsMessenger.Core/ViewModels/ConversationsViewModel.cs` (exclude trashed threads, add `TrashThreadCommand`)
- Modify: `tests/SmsMessenger.Core.Tests/ViewModels/ConversationsViewModelTests.cs`
- Create: `src/SmsMessenger.Core/Services/TrashUndoAction.cs`
- Create: `src/SmsMessenger.Core/ViewModels/TrashViewModel.cs`
- Test: `tests/SmsMessenger.Core.Tests/Services/TrashUndoActionTests.cs`
- Test: `tests/SmsMessenger.Core.Tests/ViewModels/TrashViewModelTests.cs`
- Create: `src/SmsMessenger/Pages/Trash/TrashPage.razor`
- Modify: `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor` (long-press action sheet with a "Trash" option)
- Modify: `src/SmsMessenger/Pages/Menu/MenuPage.razor` (add the "Trash" row)
- Modify: `src/SmsMessenger/MauiProgram.cs` (register `ITrashRepository`, `IUndoStack`, `TrashViewModel`; initialize the database)

**Interfaces:**
- Consumes: `ITrashRepository`, `IUndoStack` (Task 1, Task 2).
- Produces: `ConversationsViewModel`'s new constructor shape `ConversationsViewModel(IThreadService, ITrashRepository, IUndoStack)` and its `TrashThreadCommand(long threadId)` — Task 5 extends this constructor further, so its exact shape matters for that task. `TrashViewModel` (`ObservableCollection<SmsThread> TrashedThreads`, `LoadCommand`, `RestoreCommand(long threadId)`) is consumed by `TrashPage.razor`.

- [ ] **Step 1: Write the failing tests for `TrashUndoAction`**

`tests/SmsMessenger.Core.Tests/Services/TrashUndoActionTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.Tests.Services;

public class TrashUndoActionTests
{
    [Fact]
    public async Task UndoAsync_restores_the_trashed_thread()
    {
        var repository = new Mock<ITrashRepository>();
        var action = new TrashUndoAction(42, repository.Object);

        await action.UndoAsync();

        repository.Verify(r => r.RestoreThreadAsync(42), Times.Once);
    }

    [Fact]
    public void Description_is_human_readable()
    {
        var repository = new Mock<ITrashRepository>();
        var action = new TrashUndoAction(42, repository.Object);

        Assert.Equal("Trashed a conversation", action.Description);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter TrashUndoActionTests
```

Expected: FAIL — `TrashUndoAction` does not exist yet.

- [ ] **Step 3: Implement `TrashUndoAction`**

`src/SmsMessenger.Core/Services/TrashUndoAction.cs`:

```csharp
using SmsMessenger.Core.Data;

namespace SmsMessenger.Core.Services;

public class TrashUndoAction : IUndoableAction
{
    private readonly long _threadId;
    private readonly ITrashRepository _trashRepository;

    public TrashUndoAction(long threadId, ITrashRepository trashRepository)
    {
        _threadId = threadId;
        _trashRepository = trashRepository;
    }

    public string Description => "Trashed a conversation";

    public Task UndoAsync() => _trashRepository.RestoreThreadAsync(_threadId);
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter TrashUndoActionTests
```

Expected: PASS (2 tests).

- [ ] **Step 5: Update `ConversationsViewModel`'s tests for the new constructor and behavior**

Replace the full contents of `tests/SmsMessenger.Core.Tests/ViewModels/ConversationsViewModelTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ConversationsViewModelTests
{
    private static SmsThread MakeThread(long id, string address, string? name, string lastMessage) => new()
    {
        Id = id,
        Address = address,
        DisplayName = name,
        LastMessageBody = lastMessage,
        LastMessageTimestamp = DateTimeOffset.UtcNow,
        UnreadCount = 0
    };

    private static Mock<ITrashRepository> MakeEmptyTrashRepository()
    {
        var repository = new Mock<ITrashRepository>();
        repository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    [Fact]
    public async Task LoadCommand_populates_Threads_from_the_service()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, new Mock<IUndoStack>().Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Threads.Count);
    }

    [Fact]
    public async Task LoadCommand_excludes_trashed_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var viewModel = new ConversationsViewModel(threadService.Object, trashRepository.Object, new Mock<IUndoStack>().Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task TrashThreadCommand_trashes_the_thread_pushes_an_undo_action_and_removes_it_from_Threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var trashRepository = MakeEmptyTrashRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = new ConversationsViewModel(threadService.Object, trashRepository.Object, undoStack.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.TrashThreadCommand.ExecuteAsync(1L);

        trashRepository.Verify(r => r.TrashThreadAsync(1), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task SearchText_filters_by_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, new Mock<IUndoStack>().Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "alice";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }

    [Fact]
    public async Task SearchText_filters_by_raw_address_when_no_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, new Mock<IUndoStack>().Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "8890";

        Assert.Single(viewModel.Threads);
        Assert.Equal("5550148890", viewModel.Threads[0].Address);
    }

    [Fact]
    public async Task SearchText_filters_by_message_content()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "let's go to the gym"),
            MakeThread(2, "5550148890", "Bob Jones", "see you tomorrow")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, new Mock<IUndoStack>().Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "gym";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }
}
```

- [ ] **Step 6: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter ConversationsViewModelTests
```

Expected: FAIL — the current `ConversationsViewModel` constructor only takes one argument, and `TrashThreadCommand` doesn't exist.

- [ ] **Step 7: Update `ConversationsViewModel`**

Replace the full contents of `src/SmsMessenger.Core/ViewModels/ConversationsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class ConversationsViewModel : ObservableObject
{
    private readonly IThreadService _threadService;
    private readonly ITrashRepository _trashRepository;
    private readonly IUndoStack _undoStack;
    private IReadOnlyList<SmsThread> _allThreads = Array.Empty<SmsThread>();

    public ObservableCollection<SmsThread> Threads { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Threads))]
    private string _searchText = string.Empty;

    public ConversationsViewModel(IThreadService threadService, ITrashRepository trashRepository, IUndoStack undoStack)
    {
        _threadService = threadService;
        _trashRepository = trashRepository;
        _undoStack = undoStack;
    }

    [RelayCommand]
    private async Task Load()
    {
        var threads = await _threadService.GetThreadsAsync();
        var trashedIds = await _trashRepository.GetTrashedThreadIdsAsync();
        _allThreads = threads.Where(t => !trashedIds.Contains(t.Id)).ToList();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task TrashThread(long threadId)
    {
        await _trashRepository.TrashThreadAsync(threadId);
        _undoStack.Push(new TrashUndoAction(threadId, _trashRepository));
        await Load();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Threads.Clear();
        var query = SearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allThreads
            : _allThreads.Where(t =>
                t.DisplayNameOrAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.LastMessageBody.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var thread in matches)
        {
            Threads.Add(thread);
        }
    }
}
```

- [ ] **Step 8: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter ConversationsViewModelTests
```

Expected: PASS (7 tests).

- [ ] **Step 9: Write the failing tests for `TrashViewModel`**

`tests/SmsMessenger.Core.Tests/ViewModels/TrashViewModelTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class TrashViewModelTests
{
    private static SmsThread MakeThread(long id, string address) => new()
    {
        Id = id,
        Address = address,
        DisplayName = null,
        LastMessageBody = "hi",
        LastMessageTimestamp = DateTimeOffset.UtcNow,
        UnreadCount = 0
    };

    [Fact]
    public async Task LoadCommand_only_includes_trashed_threads()
    {
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231"),
            MakeThread(2, "5550148890")
        });
        var viewModel = new TrashViewModel(trashRepository.Object, threadService.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.TrashedThreads);
        Assert.Equal(2, viewModel.TrashedThreads[0].Id);
    }

    [Fact]
    public async Task RestoreCommand_removes_the_thread_from_trash_and_reloads()
    {
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.SetupSequence(r => r.GetTrashedThreadIdsAsync())
            .ReturnsAsync(new List<long> { 2 })
            .ReturnsAsync(new List<long>());
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(2, "5550148890") });
        var viewModel = new TrashViewModel(trashRepository.Object, threadService.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RestoreCommand.ExecuteAsync(2L);

        trashRepository.Verify(r => r.RestoreThreadAsync(2), Times.Once);
        Assert.Empty(viewModel.TrashedThreads);
    }
}
```

- [ ] **Step 10: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter TrashViewModelTests
```

Expected: FAIL — `TrashViewModel` does not exist yet.

- [ ] **Step 11: Implement `TrashViewModel`**

`src/SmsMessenger.Core/ViewModels/TrashViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class TrashViewModel : ObservableObject
{
    private readonly ITrashRepository _trashRepository;
    private readonly IThreadService _threadService;

    public ObservableCollection<SmsThread> TrashedThreads { get; } = new();

    public TrashViewModel(ITrashRepository trashRepository, IThreadService threadService)
    {
        _trashRepository = trashRepository;
        _threadService = threadService;
    }

    [RelayCommand]
    private async Task Load()
    {
        TrashedThreads.Clear();
        var trashedIds = await _trashRepository.GetTrashedThreadIdsAsync();
        if (trashedIds.Count == 0)
        {
            return;
        }

        var allThreads = await _threadService.GetThreadsAsync();
        foreach (var thread in allThreads.Where(t => trashedIds.Contains(t.Id)))
        {
            TrashedThreads.Add(thread);
        }
    }

    [RelayCommand]
    private async Task Restore(long threadId)
    {
        await _trashRepository.RestoreThreadAsync(threadId);
        await Load();
    }
}
```

- [ ] **Step 12: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter TrashViewModelTests
```

Expected: PASS (2 tests).

- [ ] **Step 13: Add the long-press action sheet to Conversations**

Replace the full contents of `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor`:

```razor
@page "/conversations"
@using Microsoft.AspNetCore.Components
@inject SmsMessenger.Core.ViewModels.ConversationsViewModel ViewModel
@inject NavigationManager Nav

<div style="padding:12px;">
    <div style="display:flex;justify-content:flex-end;margin-bottom:8px;">
        <button @onclick="@(() => Nav.NavigateTo("/menu"))"
                style="width:40px;height:40px;border-radius:50%;border:none;background:#e2e8f0;font-size:1.2em;">
            ⚙
        </button>
    </div>

    <input placeholder="Search contacts or numbers" @bind="ViewModel.SearchText" @bind:event="oninput" />

    @if (ViewModel.Threads.Count == 0)
    {
        <p>No conversations yet.</p>
    }
    else
    {
        @foreach (var thread in ViewModel.Threads)
        {
            <div @onclick="() => OpenThread(thread.Id, thread.Address)"
                 @onpointerdown="() => OnPointerDown(thread.Id, thread.Address)"
                 @onpointerup="() => OnPointerUp(thread.Id, thread.Address)"
                 @onpointerleave="CancelLongPress"
                 style="padding:10px;border-bottom:1px solid #eee;">
                <strong>@thread.DisplayNameOrAddress</strong>
                @if (thread.UnreadCount > 0)
                {
                    <span style="color:#06b6d4;"> ● @thread.UnreadCount</span>
                }
                <div style="color:#94a3b8;font-size:0.85em;">@thread.PreviewText</div>
            </div>
        }
    }

    <button @onclick="@(() => Nav.NavigateTo("/compose"))"
            style="position:fixed;right:24px;bottom:24px;width:56px;height:56px;border-radius:50%;background:#25D366;color:white;font-size:1.5em;border:none;">
        +
    </button>
</div>

@if (_actionSheetThreadId is not null)
{
    <div style="position:fixed;inset:0;background:rgba(0,0,0,0.4);display:flex;align-items:flex-end;" @onclick="CloseActionSheet">
        <div style="background:white;width:100%;padding:12px;" @onclick:stopPropagation="true">
            <div @onclick="TrashSelectedThread" style="padding:12px;">🗑 Trash</div>
        </div>
    </div>
}

@code {
    private long? _actionSheetThreadId;
    private string _actionSheetAddress = string.Empty;
    private CancellationTokenSource? _longPressCts;
    private bool _longPressTriggered;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OpenThread(long threadId, string address)
    {
        Nav.NavigateTo($"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}");
    }

    private void OnPointerDown(long threadId, string address)
    {
        _longPressTriggered = false;
        _longPressCts = new CancellationTokenSource();
        var token = _longPressCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                _longPressTriggered = true;
                await InvokeAsync(() =>
                {
                    _actionSheetThreadId = threadId;
                    _actionSheetAddress = address;
                    StateHasChanged();
                });
            }
            catch (TaskCanceledException)
            {
                // pointer released or left before the long-press threshold — a normal tap, not a long-press
            }
        });
    }

    private void CancelLongPress() => _longPressCts?.Cancel();

    private void OnPointerUp(long threadId, string address)
    {
        _longPressCts?.Cancel();
        if (!_longPressTriggered)
        {
            OpenThread(threadId, address);
        }
    }

    private void CloseActionSheet() => _actionSheetThreadId = null;

    private async Task TrashSelectedThread()
    {
        if (_actionSheetThreadId is long id)
        {
            await ViewModel.TrashThreadCommand.ExecuteAsync(id);
        }
        CloseActionSheet();
    }
}
```

Note the row no longer has a bare `@onclick="() => OpenThread(...)"` — tapping now goes through `OnPointerUp`, which opens the thread unless a long-press already fired. This is the only way to have both a tap-to-open and a long-press-for-actions gesture on the same element without them firing together.

- [ ] **Step 14: Create the Trash page**

`src/SmsMessenger/Pages/Trash/TrashPage.razor`:

```razor
@page "/trash"
@inject SmsMessenger.Core.ViewModels.TrashViewModel ViewModel

<div style="padding:12px;">
    <h3>Trash</h3>

    @if (ViewModel.TrashedThreads.Count == 0)
    {
        <p>No trashed conversations.</p>
    }
    else
    {
        @foreach (var thread in ViewModel.TrashedThreads)
        {
            <div style="padding:10px;border-bottom:1px solid #eee;display:flex;justify-content:space-between;align-items:center;">
                <div>
                    <strong>@thread.DisplayNameOrAddress</strong>
                    <div style="color:#94a3b8;font-size:0.85em;">@thread.PreviewText</div>
                </div>
                <button @onclick="() => ViewModel.RestoreCommand.ExecuteAsync(thread.Id)">Restore</button>
            </div>
        }
    }
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
```

- [ ] **Step 15: Add the "Trash" row to the Menu page**

`src/SmsMessenger/Pages/Menu/MenuPage.razor` becomes:

```razor
@page "/menu"
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    <h3>Menu</h3>

    <div @onclick="@(() => Nav.NavigateTo("/trash"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🗑 Trash
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/settings"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        ⚙ Message Settings
    </div>
</div>
```

- [ ] **Step 16: Register DI and initialize the database**

In `src/SmsMessenger/MauiProgram.cs`, add `using SmsMessenger.Core.Data;` to the usings at the top, and inside `CreateMauiApp()` before `builder.Services.AddTransient<SettingsViewModel>();` add:

```csharp
var trashRepository = new TrashRepository(Path.Combine(FileSystem.AppDataDirectory, "SmsMessenger.db"));
trashRepository.InitializeAsync().GetAwaiter().GetResult();
builder.Services.AddSingleton<ITrashRepository>(trashRepository);
builder.Services.AddSingleton<IUndoStack, UndoStack>();
builder.Services.AddTransient<TrashViewModel>();
```

- [ ] **Step 17: Build to verify it compiles**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 18: Manual verification on the physical device**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -t:Run -f net9.0-android
```

On the device: long-press a conversation row (press and hold roughly half a second) — confirm an action sheet slides up from the bottom with "Trash". Tap it, confirm the thread disappears from Conversations immediately. Open the menu → Trash, confirm the thread appears there with a "Restore" button. Tap Restore, confirm it reappears back in Conversations. Confirm a normal (short) tap on a row still opens the thread as before.

- [ ] **Step 19: Commit**

```bash
git add src/SmsMessenger.Core/ViewModels/ConversationsViewModel.cs tests/SmsMessenger.Core.Tests/ViewModels/ConversationsViewModelTests.cs src/SmsMessenger.Core/Services/TrashUndoAction.cs tests/SmsMessenger.Core.Tests/Services/TrashUndoActionTests.cs src/SmsMessenger.Core/ViewModels/TrashViewModel.cs tests/SmsMessenger.Core.Tests/ViewModels/TrashViewModelTests.cs src/SmsMessenger/Pages/Trash src/SmsMessenger/Pages/Conversations/ConversationsPage.razor src/SmsMessenger/Pages/Menu/MenuPage.razor src/SmsMessenger/MauiProgram.cs
git commit -m "feat: add Trash — long-press a conversation to trash it, view and restore from the menu"
```

---

### Task 5: Blocked feature

**Files:**
- Create: `src/SmsMessenger.Core/Services/IContactBlockService.cs`
- Create: `src/SmsMessenger/Platforms/Android/ContactBlockService.cs`
- Create: `src/SmsMessenger.Core/Services/BlockUndoAction.cs`
- Test: `tests/SmsMessenger.Core.Tests/Services/BlockUndoActionTests.cs`
- Modify: `src/SmsMessenger.Core/ViewModels/ConversationsViewModel.cs` (exclude blocked threads, add `BlockThreadCommand`)
- Modify: `tests/SmsMessenger.Core.Tests/ViewModels/ConversationsViewModelTests.cs`
- Create: `src/SmsMessenger.Core/ViewModels/BlockedViewModel.cs`
- Test: `tests/SmsMessenger.Core.Tests/ViewModels/BlockedViewModelTests.cs`
- Create: `src/SmsMessenger/Pages/Blocked/BlockedPage.razor`
- Modify: `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor` (add a "Block" option to the action sheet)
- Modify: `src/SmsMessenger/Pages/Menu/MenuPage.razor` (add the "Blocked" row)
- Modify: `src/SmsMessenger/MauiProgram.cs` (register `IBlockedNumberRepository`, `IContactBlockService`, `BlockedViewModel`)

**Interfaces:**
- Consumes: `IBlockedNumberRepository` (Task 1), `IUndoStack` (Task 2), `PhoneNumberFormatter.ToDisplayFormat` (original plan's Task 7).
- Produces: `IContactBlockService` (`BlockAsync(string)`, `UnblockAsync(string)`, `IsBlockedAsync(string)`, `GetBlockedNumbersAsync(): Task<IReadOnlyList<string>>`) — the orchestrating interface that wraps both the local `IBlockedNumberRepository` and Android's `BlockedNumberContract`. `ConversationsViewModel`'s constructor becomes `ConversationsViewModel(IThreadService, ITrashRepository, IContactBlockService, IUndoStack)`; every later task that constructs it directly (there are none after this one) must use this exact shape. `BlockedViewModel` (`ObservableCollection<string> BlockedNumbers`, `LoadCommand`, `UnblockCommand(string phoneNumber)`) is consumed by `BlockedPage.razor`.

- [ ] **Step 1: Write the failing tests for `BlockUndoAction`**

`tests/SmsMessenger.Core.Tests/Services/BlockUndoActionTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.Tests.Services;

public class BlockUndoActionTests
{
    [Fact]
    public async Task UndoAsync_unblocks_the_number()
    {
        var blockService = new Mock<IContactBlockService>();
        var action = new BlockUndoAction("5550142231", blockService.Object);

        await action.UndoAsync();

        blockService.Verify(s => s.UnblockAsync("5550142231"), Times.Once);
    }

    [Fact]
    public void Description_is_human_readable()
    {
        var blockService = new Mock<IContactBlockService>();
        var action = new BlockUndoAction("5550142231", blockService.Object);

        Assert.Equal("Blocked a contact", action.Description);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter BlockUndoActionTests
```

Expected: FAIL — `IContactBlockService` and `BlockUndoAction` don't exist yet.

- [ ] **Step 3: Implement `IContactBlockService` and `BlockUndoAction`**

`src/SmsMessenger.Core/Services/IContactBlockService.cs`:

```csharp
namespace SmsMessenger.Core.Services;

public interface IContactBlockService
{
    Task BlockAsync(string phoneNumber);
    Task UnblockAsync(string phoneNumber);
    Task<bool> IsBlockedAsync(string phoneNumber);
    Task<IReadOnlyList<string>> GetBlockedNumbersAsync();
}
```

`src/SmsMessenger.Core/Services/BlockUndoAction.cs`:

```csharp
namespace SmsMessenger.Core.Services;

public class BlockUndoAction : IUndoableAction
{
    private readonly string _phoneNumber;
    private readonly IContactBlockService _blockService;

    public BlockUndoAction(string phoneNumber, IContactBlockService blockService)
    {
        _phoneNumber = phoneNumber;
        _blockService = blockService;
    }

    public string Description => "Blocked a contact";

    public Task UndoAsync() => _blockService.UnblockAsync(_phoneNumber);
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter BlockUndoActionTests
```

Expected: PASS (2 tests).

- [ ] **Step 5: Implement the Android `ContactBlockService`**

`src/SmsMessenger/Platforms/Android/ContactBlockService.cs`:

```csharp
using Android.Content;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidBlockedNumberContract = global::Android.Provider.BlockedNumberContract;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace SmsMessenger.Platforms.Android;

public class ContactBlockService : IContactBlockService
{
    private readonly IBlockedNumberRepository _repository;

    public ContactBlockService(IBlockedNumberRepository repository)
    {
        _repository = repository;
    }

    public async Task BlockAsync(string phoneNumber)
    {
        await _repository.BlockAsync(phoneNumber);

        var context = AndroidApp.Context;
        var values = new AndroidContentValues();
        values.Put(AndroidBlockedNumberContract.BlockedNumbers.ColumnOriginalNumber, phoneNumber);
        context.ContentResolver!.Insert(AndroidBlockedNumberContract.BlockedNumbers.ContentUri!, values);
    }

    public async Task UnblockAsync(string phoneNumber)
    {
        await _repository.UnblockAsync(phoneNumber);

        AndroidBlockedNumberContract.Unblock(AndroidApp.Context, phoneNumber);
    }

    public Task<bool> IsBlockedAsync(string phoneNumber) => _repository.IsBlockedAsync(phoneNumber);

    public async Task<IReadOnlyList<string>> GetBlockedNumbersAsync()
    {
        var rows = await _repository.GetBlockedNumbersAsync();
        return rows.Select(r => r.PhoneNumber).ToList();
    }
}
```

`BlockedNumberContract.BlockedNumbers.ColumnOriginalNumber` and `BlockedNumberContract.Unblock(Context, string)` are this task's one open risk (flagged during design): the exact static member names in the .NET-for-Android binding may differ slightly from this guess (e.g. nested under an `InterfaceConsts` class, the way `ContactService.cs` sometimes needs `AndroidContactsContract.PhoneLookup.InterfaceConsts.DisplayName` instead of the direct name — compare that file if this doesn't compile). If the build error names the actual member, fix the two lines above to match it; the rest of the class is unaffected. If `BlockedNumberContract` access throws a `SecurityException` at runtime on this device even though we're the default SMS app, remove the two `AndroidBlockedNumberContract` lines and keep everything else — the feature still fully works via the local `IBlockedNumberRepository` filtering alone, just without OS-level enforcement; note this in a commit message if it happens so a future task can revisit it.

- [ ] **Step 6: Update `ConversationsViewModel`'s tests for the new constructor and blocked-filtering behavior**

Replace the full contents of `tests/SmsMessenger.Core.Tests/ViewModels/ConversationsViewModelTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ConversationsViewModelTests
{
    private static SmsThread MakeThread(long id, string address, string? name, string lastMessage) => new()
    {
        Id = id,
        Address = address,
        DisplayName = name,
        LastMessageBody = lastMessage,
        LastMessageTimestamp = DateTimeOffset.UtcNow,
        UnreadCount = 0
    };

    private static Mock<ITrashRepository> MakeEmptyTrashRepository()
    {
        var repository = new Mock<ITrashRepository>();
        repository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static Mock<IContactBlockService> MakeEmptyBlockService()
    {
        var service = new Mock<IContactBlockService>();
        service.Setup(s => s.GetBlockedNumbersAsync()).ReturnsAsync(new List<string>());
        return service;
    }

    [Fact]
    public async Task LoadCommand_populates_Threads_from_the_service()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Threads.Count);
    }

    [Fact]
    public async Task LoadCommand_excludes_trashed_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var viewModel = new ConversationsViewModel(threadService.Object, trashRepository.Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task LoadCommand_excludes_blocked_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var blockService = new Mock<IContactBlockService>();
        blockService.Setup(s => s.GetBlockedNumbersAsync()).ReturnsAsync(new List<string> { "5550148890" });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, blockService.Object, new Mock<IUndoStack>().Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task TrashThreadCommand_trashes_the_thread_pushes_an_undo_action_and_removes_it_from_Threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var trashRepository = MakeEmptyTrashRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = new ConversationsViewModel(threadService.Object, trashRepository.Object, MakeEmptyBlockService().Object, undoStack.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.TrashThreadCommand.ExecuteAsync(1L);

        trashRepository.Verify(r => r.TrashThreadAsync(1), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task BlockThreadCommand_blocks_the_address_pushes_an_undo_action_and_removes_it_from_Threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var blockService = MakeEmptyBlockService();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, blockService.Object, undoStack.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.BlockThreadCommand.ExecuteAsync("5550142231");

        blockService.Verify(s => s.BlockAsync("5550142231"), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task SearchText_filters_by_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "alice";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }

    [Fact]
    public async Task SearchText_filters_by_raw_address_when_no_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "8890";

        Assert.Single(viewModel.Threads);
        Assert.Equal("5550148890", viewModel.Threads[0].Address);
    }

    [Fact]
    public async Task SearchText_filters_by_message_content()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "let's go to the gym"),
            MakeThread(2, "5550148890", "Bob Jones", "see you tomorrow")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "gym";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter ConversationsViewModelTests
```

Expected: FAIL — the constructor doesn't take an `IContactBlockService` yet, and `BlockThreadCommand` doesn't exist.

- [ ] **Step 8: Update `ConversationsViewModel`**

Replace the full contents of `src/SmsMessenger.Core/ViewModels/ConversationsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class ConversationsViewModel : ObservableObject
{
    private readonly IThreadService _threadService;
    private readonly ITrashRepository _trashRepository;
    private readonly IContactBlockService _blockService;
    private readonly IUndoStack _undoStack;
    private IReadOnlyList<SmsThread> _allThreads = Array.Empty<SmsThread>();

    public ObservableCollection<SmsThread> Threads { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Threads))]
    private string _searchText = string.Empty;

    public ConversationsViewModel(
        IThreadService threadService,
        ITrashRepository trashRepository,
        IContactBlockService blockService,
        IUndoStack undoStack)
    {
        _threadService = threadService;
        _trashRepository = trashRepository;
        _blockService = blockService;
        _undoStack = undoStack;
    }

    [RelayCommand]
    private async Task Load()
    {
        var threads = await _threadService.GetThreadsAsync();
        var trashedIds = await _trashRepository.GetTrashedThreadIdsAsync();
        var blockedNumbers = await _blockService.GetBlockedNumbersAsync();
        _allThreads = threads
            .Where(t => !trashedIds.Contains(t.Id) && !blockedNumbers.Contains(t.Address))
            .ToList();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task TrashThread(long threadId)
    {
        await _trashRepository.TrashThreadAsync(threadId);
        _undoStack.Push(new TrashUndoAction(threadId, _trashRepository));
        await Load();
    }

    [RelayCommand]
    private async Task BlockThread(string address)
    {
        await _blockService.BlockAsync(address);
        _undoStack.Push(new BlockUndoAction(address, _blockService));
        await Load();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Threads.Clear();
        var query = SearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allThreads
            : _allThreads.Where(t =>
                t.DisplayNameOrAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.LastMessageBody.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var thread in matches)
        {
            Threads.Add(thread);
        }
    }
}
```

- [ ] **Step 9: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter ConversationsViewModelTests
```

Expected: PASS (9 tests).

- [ ] **Step 10: Write the failing tests for `BlockedViewModel`**

`tests/SmsMessenger.Core.Tests/ViewModels/BlockedViewModelTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class BlockedViewModelTests
{
    [Fact]
    public async Task LoadCommand_populates_BlockedNumbers()
    {
        var blockService = new Mock<IContactBlockService>();
        blockService.Setup(s => s.GetBlockedNumbersAsync()).ReturnsAsync(new List<string> { "5550142231" });
        var viewModel = new BlockedViewModel(blockService.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.BlockedNumbers);
        Assert.Equal("5550142231", viewModel.BlockedNumbers[0]);
    }

    [Fact]
    public async Task UnblockCommand_unblocks_and_reloads()
    {
        var blockService = new Mock<IContactBlockService>();
        blockService.SetupSequence(s => s.GetBlockedNumbersAsync())
            .ReturnsAsync(new List<string> { "5550142231" })
            .ReturnsAsync(new List<string>());
        var viewModel = new BlockedViewModel(blockService.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.UnblockCommand.ExecuteAsync("5550142231");

        blockService.Verify(s => s.UnblockAsync("5550142231"), Times.Once);
        Assert.Empty(viewModel.BlockedNumbers);
    }
}
```

- [ ] **Step 11: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter BlockedViewModelTests
```

Expected: FAIL — `BlockedViewModel` does not exist yet.

- [ ] **Step 12: Implement `BlockedViewModel`**

`src/SmsMessenger.Core/ViewModels/BlockedViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class BlockedViewModel : ObservableObject
{
    private readonly IContactBlockService _blockService;

    public ObservableCollection<string> BlockedNumbers { get; } = new();

    public BlockedViewModel(IContactBlockService blockService)
    {
        _blockService = blockService;
    }

    [RelayCommand]
    private async Task Load()
    {
        BlockedNumbers.Clear();
        foreach (var number in await _blockService.GetBlockedNumbersAsync())
        {
            BlockedNumbers.Add(number);
        }
    }

    [RelayCommand]
    private async Task Unblock(string phoneNumber)
    {
        await _blockService.UnblockAsync(phoneNumber);
        await Load();
    }
}
```

- [ ] **Step 13: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter BlockedViewModelTests
```

Expected: PASS (2 tests).

- [ ] **Step 14: Add "Block" to the Conversations action sheet**

In `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor`, add a second row inside the action sheet `<div>` (right after the "🗑 Trash" row):

```razor
<div @onclick="BlockSelectedThread" style="padding:12px;">🚫 Block</div>
```

And add the matching method in `@code`, right after `TrashSelectedThread`:

```csharp
private async Task BlockSelectedThread()
{
    if (_actionSheetThreadId is not null)
    {
        await ViewModel.BlockThreadCommand.ExecuteAsync(_actionSheetAddress);
    }
    CloseActionSheet();
}
```

- [ ] **Step 15: Create the Blocked page**

`src/SmsMessenger/Pages/Blocked/BlockedPage.razor`:

```razor
@page "/blocked"
@inject SmsMessenger.Core.ViewModels.BlockedViewModel ViewModel

<div style="padding:12px;">
    <h3>Blocked</h3>

    @if (ViewModel.BlockedNumbers.Count == 0)
    {
        <p>No blocked numbers.</p>
    }
    else
    {
        @foreach (var number in ViewModel.BlockedNumbers)
        {
            <div style="padding:10px;border-bottom:1px solid #eee;display:flex;justify-content:space-between;align-items:center;">
                <span>@SmsMessenger.Core.Utils.PhoneNumberFormatter.ToDisplayFormat(number)</span>
                <button @onclick="() => ViewModel.UnblockCommand.ExecuteAsync(number)">Unblock</button>
            </div>
        }
    }
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
```

- [ ] **Step 16: Add the "Blocked" row to the Menu page**

`src/SmsMessenger/Pages/Menu/MenuPage.razor` becomes:

```razor
@page "/menu"
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    <h3>Menu</h3>

    <div @onclick="@(() => Nav.NavigateTo("/trash"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🗑 Trash
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/blocked"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🚫 Blocked
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/settings"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        ⚙ Message Settings
    </div>
</div>
```

- [ ] **Step 17: Register DI**

In `src/SmsMessenger/MauiProgram.cs`, after the `trashRepository`/`ITrashRepository` block added in Task 4, add:

```csharp
var blockedNumberRepository = new BlockedNumberRepository(Path.Combine(FileSystem.AppDataDirectory, "SmsMessenger.db"));
blockedNumberRepository.InitializeAsync().GetAwaiter().GetResult();
builder.Services.AddSingleton<IBlockedNumberRepository>(blockedNumberRepository);
builder.Services.AddSingleton<IContactBlockService, ContactBlockService>();
builder.Services.AddTransient<BlockedViewModel>();
```

(Both repositories point at the same `SmsMessenger.db` file — `sqlite-net-pcl` supports multiple `SQLiteAsyncConnection` instances safely sharing one file, so no shared-connection abstraction is needed for two small tables.)

- [ ] **Step 18: Build to verify it compiles**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -f net9.0-android
```

Expected: `Build succeeded.` If `BlockedNumberContract` member names need adjusting per Step 5's note, fix them now.

- [ ] **Step 19: Manual verification on the physical device**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -t:Run -f net9.0-android
```

On the device: long-press a conversation, tap "Block". Confirm the thread disappears from Conversations. Open the menu → Blocked, confirm the number is listed. Have that number text you again — confirm no new message/notification arrives at all (OS-level block working) or, if it does arrive but the thread stays hidden from Conversations, the local filtering fallback is working (note which happened). Tap Unblock, confirm the number can text you again and re-appears normally once it does.

- [ ] **Step 20: Commit**

```bash
git add src/SmsMessenger.Core/Services/IContactBlockService.cs src/SmsMessenger/Platforms/Android/ContactBlockService.cs src/SmsMessenger.Core/Services/BlockUndoAction.cs tests/SmsMessenger.Core.Tests/Services/BlockUndoActionTests.cs src/SmsMessenger.Core/ViewModels/ConversationsViewModel.cs tests/SmsMessenger.Core.Tests/ViewModels/ConversationsViewModelTests.cs src/SmsMessenger.Core/ViewModels/BlockedViewModel.cs tests/SmsMessenger.Core.Tests/ViewModels/BlockedViewModelTests.cs src/SmsMessenger/Pages/Blocked src/SmsMessenger/Pages/Conversations/ConversationsPage.razor src/SmsMessenger/Pages/Menu/MenuPage.razor src/SmsMessenger/MauiProgram.cs
git commit -m "feat: add Blocked — long-press a conversation to block it via BlockedNumberContract"
```

---

### Task 6: Mark as Read and Undo wiring

**Files:**
- Create: `src/SmsMessenger.Core/Services/IMarkAsReadService.cs`
- Create: `src/SmsMessenger/Platforms/Android/MarkAsReadService.cs`
- Create: `src/SmsMessenger.Core/Services/MarkAsReadUndoAction.cs`
- Test: `tests/SmsMessenger.Core.Tests/Services/MarkAsReadUndoActionTests.cs`
- Create: `src/SmsMessenger.Core/ViewModels/MenuViewModel.cs`
- Test: `tests/SmsMessenger.Core.Tests/ViewModels/MenuViewModelTests.cs`
- Modify: `src/SmsMessenger/Pages/Menu/MenuPage.razor` (inject `MenuViewModel`, add "Mark as Read" and "Undo" rows)
- Modify: `src/SmsMessenger/MauiProgram.cs` (register `IMarkAsReadService`, `MenuViewModel`)

**Interfaces:**
- Consumes: `IUndoStack` (Task 2), `IThreadService` (original plan's Task 8).
- Produces: `IMarkAsReadService` (`MarkAllAsReadAsync(): Task<IReadOnlyList<long>>` — returns the thread IDs that *were* unread, for undo; `MarkThreadsAsUnreadAsync(IReadOnlyList<long>): Task`). `MenuViewModel` (`StatusMessage: string`, `MarkAllAsReadCommand`, `UndoCommand`) is consumed by `MenuPage.razor` and is the last new view model this plan introduces — every menu action from here on routes through it.

- [ ] **Step 1: Write the failing tests for `MarkAsReadUndoAction`**

`tests/SmsMessenger.Core.Tests/Services/MarkAsReadUndoActionTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.Tests.Services;

public class MarkAsReadUndoActionTests
{
    [Fact]
    public async Task UndoAsync_marks_the_original_threads_unread_again()
    {
        var service = new Mock<IMarkAsReadService>();
        var action = new MarkAsReadUndoAction(new List<long> { 1, 2 }, service.Object);

        await action.UndoAsync();

        service.Verify(s => s.MarkThreadsAsUnreadAsync(It.Is<IReadOnlyList<long>>(ids => ids.SequenceEqual(new long[] { 1, 2 }))), Times.Once);
    }

    [Fact]
    public void Description_reports_the_thread_count()
    {
        var service = new Mock<IMarkAsReadService>();
        var action = new MarkAsReadUndoAction(new List<long> { 1, 2, 3 }, service.Object);

        Assert.Equal("Marked 3 conversation(s) as read", action.Description);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter MarkAsReadUndoActionTests
```

Expected: FAIL — `IMarkAsReadService` and `MarkAsReadUndoAction` don't exist yet.

- [ ] **Step 3: Implement `IMarkAsReadService` and `MarkAsReadUndoAction`**

`src/SmsMessenger.Core/Services/IMarkAsReadService.cs`:

```csharp
namespace SmsMessenger.Core.Services;

public interface IMarkAsReadService
{
    Task<IReadOnlyList<long>> MarkAllAsReadAsync();
    Task MarkThreadsAsUnreadAsync(IReadOnlyList<long> threadIds);
}
```

`src/SmsMessenger.Core/Services/MarkAsReadUndoAction.cs`:

```csharp
namespace SmsMessenger.Core.Services;

public class MarkAsReadUndoAction : IUndoableAction
{
    private readonly IReadOnlyList<long> _threadIds;
    private readonly IMarkAsReadService _markAsReadService;

    public MarkAsReadUndoAction(IReadOnlyList<long> threadIds, IMarkAsReadService markAsReadService)
    {
        _threadIds = threadIds;
        _markAsReadService = markAsReadService;
    }

    public string Description => $"Marked {_threadIds.Count} conversation(s) as read";

    public Task UndoAsync() => _markAsReadService.MarkThreadsAsUnreadAsync(_threadIds);
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter MarkAsReadUndoActionTests
```

Expected: PASS (2 tests).

- [ ] **Step 5: Implement the Android `MarkAsReadService`**

`src/SmsMessenger/Platforms/Android/MarkAsReadService.cs`:

```csharp
using SmsMessenger.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace SmsMessenger.Platforms.Android;

public class MarkAsReadService : IMarkAsReadService
{
    private readonly IThreadService _threadService;

    public MarkAsReadService(IThreadService threadService)
    {
        _threadService = threadService;
    }

    public async Task<IReadOnlyList<long>> MarkAllAsReadAsync()
    {
        var threads = await _threadService.GetThreadsAsync();
        var unreadThreadIds = threads.Where(t => t.UnreadCount > 0).Select(t => t.Id).ToList();

        SetReadFlag(unreadThreadIds, read: 1);

        return unreadThreadIds;
    }

    public Task MarkThreadsAsUnreadAsync(IReadOnlyList<long> threadIds)
    {
        SetReadFlag(threadIds, read: 0);
        return Task.CompletedTask;
    }

    private static void SetReadFlag(IReadOnlyList<long> threadIds, int read)
    {
        var context = AndroidApp.Context;
        var values = new AndroidContentValues();
        values.Put("read", read);

        foreach (var threadId in threadIds)
        {
            context.ContentResolver!.Update(AndroidTelephony.Sms.ContentUri!, values, "thread_id = ?", new[] { threadId.ToString() });
        }
    }
}
```

- [ ] **Step 6: Write the failing tests for `MenuViewModel`**

`tests/SmsMessenger.Core.Tests/ViewModels/MenuViewModelTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class MenuViewModelTests
{
    [Fact]
    public async Task MarkAllAsReadCommand_pushes_an_undo_action_and_reports_the_count()
    {
        var markAsRead = new Mock<IMarkAsReadService>();
        markAsRead.Setup(s => s.MarkAllAsReadAsync()).ReturnsAsync(new List<long> { 1, 2 });
        var undoStack = new Mock<IUndoStack>();
        var viewModel = new MenuViewModel(markAsRead.Object, undoStack.Object);

        await viewModel.MarkAllAsReadCommand.ExecuteAsync(null);

        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
        Assert.Equal("Marked 2 conversation(s) as read", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UndoCommand_reports_what_was_undone()
    {
        var markAsRead = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        undoStack.Setup(s => s.UndoAsync()).ReturnsAsync("Trashed a conversation");
        var viewModel = new MenuViewModel(markAsRead.Object, undoStack.Object);

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Undid: Trashed a conversation", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UndoCommand_reports_nothing_to_undo_when_stack_is_empty()
    {
        var markAsRead = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        undoStack.Setup(s => s.UndoAsync()).ReturnsAsync((string?)null);
        var viewModel = new MenuViewModel(markAsRead.Object, undoStack.Object);

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Nothing to undo", viewModel.StatusMessage);
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter MenuViewModelTests
```

Expected: FAIL — `MenuViewModel` does not exist yet.

- [ ] **Step 8: Implement `MenuViewModel`**

`src/SmsMessenger.Core/ViewModels/MenuViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class MenuViewModel : ObservableObject
{
    private readonly IMarkAsReadService _markAsReadService;
    private readonly IUndoStack _undoStack;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public MenuViewModel(IMarkAsReadService markAsReadService, IUndoStack undoStack)
    {
        _markAsReadService = markAsReadService;
        _undoStack = undoStack;
    }

    [RelayCommand]
    private async Task MarkAllAsRead()
    {
        var threadIds = await _markAsReadService.MarkAllAsReadAsync();
        _undoStack.Push(new MarkAsReadUndoAction(threadIds, _markAsReadService));
        StatusMessage = $"Marked {threadIds.Count} conversation(s) as read";
    }

    [RelayCommand]
    private async Task Undo()
    {
        var description = await _undoStack.UndoAsync();
        StatusMessage = description is null ? "Nothing to undo" : $"Undid: {description}";
    }
}
```

- [ ] **Step 9: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter MenuViewModelTests
```

Expected: PASS (3 tests).

- [ ] **Step 10: Wire `MenuViewModel` into the Menu page**

`src/SmsMessenger/Pages/Menu/MenuPage.razor` becomes:

```razor
@page "/menu"
@inject SmsMessenger.Core.ViewModels.MenuViewModel ViewModel
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    <h3>Menu</h3>

    @if (!string.IsNullOrEmpty(ViewModel.StatusMessage))
    {
        <p style="color:#06b6d4;">@ViewModel.StatusMessage</p>
    }

    <div @onclick="@(() => Nav.NavigateTo("/trash"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🗑 Trash
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/blocked"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🚫 Blocked
    </div>
    <div @onclick="() => ViewModel.MarkAllAsReadCommand.ExecuteAsync(null)" style="padding:12px 0;border-bottom:1px solid #eee;">
        ✓ Mark as Read
    </div>
    <div @onclick="() => ViewModel.UndoCommand.ExecuteAsync(null)" style="padding:12px 0;border-bottom:1px solid #eee;">
        ↩ Undo
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/settings"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        ⚙ Message Settings
    </div>
</div>
```

- [ ] **Step 11: Register DI**

In `src/SmsMessenger/MauiProgram.cs`, after the `IContactBlockService` line added in Task 5, add:

```csharp
builder.Services.AddSingleton<IMarkAsReadService, MarkAsReadService>();
builder.Services.AddTransient<MenuViewModel>();
```

- [ ] **Step 12: Build to verify it compiles**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 13: Manual verification on the physical device**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -t:Run -f net9.0-android
```

On the device: with at least one unread conversation showing the unread dot, open the menu and tap "Mark as Read" — confirm a status line appears ("Marked N conversation(s) as read") and the unread dot disappears from Conversations. Tap "Undo" — confirm the status line changes to "Undid: Marked N conversation(s) as read" and the unread dot comes back. Trash a conversation, go to the menu, tap Undo — confirm it says "Undid: Trashed a conversation" and the thread reappears in Conversations. Tap Undo again with nothing left to undo — confirm it says "Nothing to undo".

- [ ] **Step 14: Commit**

```bash
git add src/SmsMessenger.Core/Services/IMarkAsReadService.cs src/SmsMessenger/Platforms/Android/MarkAsReadService.cs src/SmsMessenger.Core/Services/MarkAsReadUndoAction.cs tests/SmsMessenger.Core.Tests/Services/MarkAsReadUndoActionTests.cs src/SmsMessenger.Core/ViewModels/MenuViewModel.cs tests/SmsMessenger.Core.Tests/ViewModels/MenuViewModelTests.cs src/SmsMessenger/Pages/Menu/MenuPage.razor src/SmsMessenger/MauiProgram.cs
git commit -m "feat: add Mark as Read and wire Undo into the menu"
```

---

### Task 7: Theme

**Files:**
- Create: `src/SmsMessenger.Core/Models/ThemeMode.cs`
- Create: `src/SmsMessenger.Core/Services/IThemeService.cs`
- Create: `src/SmsMessenger/Platforms/Android/ThemeService.cs`
- Create: `src/SmsMessenger.Core/ViewModels/ThemeViewModel.cs`
- Test: `tests/SmsMessenger.Core.Tests/ViewModels/ThemeViewModelTests.cs`
- Create: `src/SmsMessenger/wwwroot/js/theme.js`
- Modify: `src/SmsMessenger/wwwroot/index.html` (reference `theme.js`)
- Modify: `src/SmsMessenger/wwwroot/css/app.css` (append theme CSS variables)
- Create: `src/SmsMessenger/Pages/Theme/ThemePage.razor`
- Modify: `src/SmsMessenger/Pages/SplashPage.razor` (apply the saved theme on every cold start)
- Modify: `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor` (FAB uses `var(--accent-color)` instead of a literal hex)
- Modify: `src/SmsMessenger/Pages/Menu/MenuPage.razor` (add the "Theme" row, in its final spec-ordered position)
- Modify: `src/SmsMessenger/MauiProgram.cs` (register `IThemeService`, `ThemeViewModel`)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `IThemeService` (`GetThemeMode(): ThemeMode`, `SetThemeMode(ThemeMode)`, `GetAccentColor(): string`, `SetAccentColor(string)`) backed by `Preferences`, following the same pattern the original plan used for its onboarding/notification flags — no new storage abstraction. `ThemeViewModel` (`SelectedMode: ThemeMode`, `AccentColor: string`, `SelectModeCommand(ThemeMode)`, `SelectAccentCommand(string)`) is consumed by `ThemePage.razor`. The `window.applyTheme(mode, accentColor)` JS function is this app's first piece of custom JS interop — called from both `ThemePage` (on selection) and `SplashPage` (on every cold start, so a relaunch respects the last choice).

This is a real, if mechanical, refactor: the app's colors have all been hardcoded hex literals inline since the original plan. This task introduces the CSS-variable system and rewires the highest-visibility surfaces (the Splash background and the Conversations FAB) as the concrete example of the pattern; any future page-specific color can follow the same `var(--accent-color)` approach.

- [ ] **Step 1: Write the failing tests for `ThemeViewModel`**

`tests/SmsMessenger.Core.Tests/ViewModels/ThemeViewModelTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ThemeViewModelTests
{
    [Fact]
    public void Constructor_loads_the_currently_saved_mode_and_accent()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(s => s.GetThemeMode()).Returns(ThemeMode.Dark);
        themeService.Setup(s => s.GetAccentColor()).Returns("#ff0000");

        var viewModel = new ThemeViewModel(themeService.Object);

        Assert.Equal(ThemeMode.Dark, viewModel.SelectedMode);
        Assert.Equal("#ff0000", viewModel.AccentColor);
    }

    [Fact]
    public void SelectModeCommand_updates_the_property_and_saves_it()
    {
        var themeService = new Mock<IThemeService>();
        var viewModel = new ThemeViewModel(themeService.Object);

        viewModel.SelectModeCommand.Execute(ThemeMode.Light);

        Assert.Equal(ThemeMode.Light, viewModel.SelectedMode);
        themeService.Verify(s => s.SetThemeMode(ThemeMode.Light), Times.Once);
    }

    [Fact]
    public void SelectAccentCommand_updates_the_property_and_saves_it()
    {
        var themeService = new Mock<IThemeService>();
        var viewModel = new ThemeViewModel(themeService.Object);

        viewModel.SelectAccentCommand.Execute("#00ff00");

        Assert.Equal("#00ff00", viewModel.AccentColor);
        themeService.Verify(s => s.SetAccentColor("#00ff00"), Times.Once);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter ThemeViewModelTests
```

Expected: FAIL — `ThemeMode`, `IThemeService`, and `ThemeViewModel` don't exist yet.

- [ ] **Step 3: Implement `ThemeMode`, `IThemeService`, and `ThemeViewModel`**

`src/SmsMessenger.Core/Models/ThemeMode.cs`:

```csharp
namespace SmsMessenger.Core.Models;

public enum ThemeMode
{
    Light,
    Dark,
    System
}
```

`src/SmsMessenger.Core/Services/IThemeService.cs`:

```csharp
using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Services;

public interface IThemeService
{
    ThemeMode GetThemeMode();
    void SetThemeMode(ThemeMode mode);
    string GetAccentColor();
    void SetAccentColor(string hexColor);
}
```

`src/SmsMessenger.Core/ViewModels/ThemeViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class ThemeViewModel : ObservableObject
{
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private ThemeMode _selectedMode;

    [ObservableProperty]
    private string _accentColor = string.Empty;

    public ThemeViewModel(IThemeService themeService)
    {
        _themeService = themeService;
        _selectedMode = _themeService.GetThemeMode();
        _accentColor = _themeService.GetAccentColor();
    }

    [RelayCommand]
    private void SelectMode(ThemeMode mode)
    {
        SelectedMode = mode;
        _themeService.SetThemeMode(mode);
    }

    [RelayCommand]
    private void SelectAccent(string hexColor)
    {
        AccentColor = hexColor;
        _themeService.SetAccentColor(hexColor);
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter ThemeViewModelTests
```

Expected: PASS (3 tests).

- [ ] **Step 5: Implement the Android `ThemeService`**

`src/SmsMessenger/Platforms/Android/ThemeService.cs`:

```csharp
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Platforms.Android;

public class ThemeService : IThemeService
{
    private const string ModeKey = "theme_mode";
    private const string AccentKey = "theme_accent";
    private const string DefaultAccent = "#25D366";

    public ThemeMode GetThemeMode()
    {
        var stored = Preferences.Get(ModeKey, nameof(ThemeMode.System));
        return Enum.TryParse<ThemeMode>(stored, out var mode) ? mode : ThemeMode.System;
    }

    public void SetThemeMode(ThemeMode mode) => Preferences.Set(ModeKey, mode.ToString());

    public string GetAccentColor() => Preferences.Get(AccentKey, DefaultAccent);

    public void SetAccentColor(string hexColor) => Preferences.Set(AccentKey, hexColor);
}
```

(`Preferences` resolves via the `Microsoft.Maui.Storage` global using already present in the app project — same as every other Android service in this codebase.)

- [ ] **Step 6: Add the theme JS interop file**

`src/SmsMessenger/wwwroot/js/theme.js`:

```javascript
window.applyTheme = function (mode, accentColor) {
    var resolvedMode = mode;
    if (mode === "System") {
        resolvedMode = (window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches) ? "Dark" : "Light";
    }
    document.body.setAttribute("data-theme", resolvedMode.toLowerCase());
    document.body.style.setProperty("--accent-color", accentColor);
};
```

In `src/SmsMessenger/wwwroot/index.html`, add a line right before `<script src="_framework/blazor.webview.js" autostart="false"></script>`:

```html
<script src="js/theme.js"></script>
```

- [ ] **Step 7: Add the theme CSS variables**

Append to the end of `src/SmsMessenger/wwwroot/css/app.css`:

```css

:root {
    --accent-color: #25D366;
    --surface-color: #ffffff;
    --text-color: #111111;
}

body[data-theme="dark"] {
    --surface-color: #121212;
    --text-color: #f1f1f1;
}

body {
    background-color: var(--surface-color);
    color: var(--text-color);
}
```

- [ ] **Step 8: Apply the saved theme on every cold start**

Replace the full contents of `src/SmsMessenger/Pages/SplashPage.razor`:

```razor
@page "/"
@inject SmsMessenger.Core.ViewModels.SplashViewModel ViewModel
@inject SmsMessenger.Core.Services.PendingNavigationStore PendingNav
@inject SmsMessenger.Core.Services.IThemeService ThemeService
@inject Microsoft.AspNetCore.Components.NavigationManager Nav
@inject Microsoft.JSInterop.IJSRuntime JS

<div style="display:flex;align-items:center;justify-content:center;height:100vh;background:var(--accent-color);">
    <h1 style="color:white;">SMS Messenger</h1>
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await JS.InvokeVoidAsync("applyTheme", ThemeService.GetThemeMode().ToString(), ThemeService.GetAccentColor());

        var pendingRoute = PendingNav.ConsumePendingRoute();
        if (!string.IsNullOrEmpty(pendingRoute))
        {
            Nav.NavigateTo(pendingRoute);
            return;
        }

        await ViewModel.InitializeAsync();
    }
}
```

- [ ] **Step 9: Rewire the Conversations FAB to the accent variable**

In `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor`, change the FAB button's style from `background:#25D366;` to `background:var(--accent-color);` (the single occurrence in that file — the search-bar and unread-dot colors are untouched by this task).

- [ ] **Step 10: Create the Theme page**

`src/SmsMessenger/Pages/Theme/ThemePage.razor`:

```razor
@page "/theme"
@inject SmsMessenger.Core.ViewModels.ThemeViewModel ViewModel
@inject Microsoft.JSInterop.IJSRuntime JS

<div style="padding:12px;">
    <h3>Theme</h3>

    <label>
        <input type="radio" name="mode" checked="@(ViewModel.SelectedMode == SmsMessenger.Core.Models.ThemeMode.Light)" @onchange="() => SelectMode(SmsMessenger.Core.Models.ThemeMode.Light)" />
        Light
    </label>
    <br />
    <label>
        <input type="radio" name="mode" checked="@(ViewModel.SelectedMode == SmsMessenger.Core.Models.ThemeMode.Dark)" @onchange="() => SelectMode(SmsMessenger.Core.Models.ThemeMode.Dark)" />
        Dark
    </label>
    <br />
    <label>
        <input type="radio" name="mode" checked="@(ViewModel.SelectedMode == SmsMessenger.Core.Models.ThemeMode.System)" @onchange="() => SelectMode(SmsMessenger.Core.Models.ThemeMode.System)" />
        System
    </label>

    <h4>Accent color</h4>
    <div style="display:flex;gap:8px;">
        @foreach (var color in _accentChoices)
        {
            <button @onclick="() => SelectAccent(color)"
                    style="width:36px;height:36px;border-radius:50%;background:@color;border:@(ViewModel.AccentColor == color ? "3px solid black" : "none");">
            </button>
        }
    </div>
</div>

@code {
    private readonly string[] _accentChoices = { "#25D366", "#3b82f6", "#a855f7", "#ef4444", "#f59e0b" };

    private async Task SelectMode(SmsMessenger.Core.Models.ThemeMode mode)
    {
        ViewModel.SelectModeCommand.Execute(mode);
        await JS.InvokeVoidAsync("applyTheme", ViewModel.SelectedMode.ToString(), ViewModel.AccentColor);
    }

    private async Task SelectAccent(string color)
    {
        ViewModel.SelectAccentCommand.Execute(color);
        await JS.InvokeVoidAsync("applyTheme", ViewModel.SelectedMode.ToString(), ViewModel.AccentColor);
    }
}
```

- [ ] **Step 11: Add the "Theme" row to the Menu page, in final spec order**

Replace the full contents of `src/SmsMessenger/Pages/Menu/MenuPage.razor`:

```razor
@page "/menu"
@inject SmsMessenger.Core.ViewModels.MenuViewModel ViewModel
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    <h3>Menu</h3>

    @if (!string.IsNullOrEmpty(ViewModel.StatusMessage))
    {
        <p style="color:#06b6d4;">@ViewModel.StatusMessage</p>
    }

    <div @onclick="@(() => Nav.NavigateTo("/theme"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🎨 Theme
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/trash"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🗑 Trash
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/blocked"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🚫 Blocked
    </div>
    <div @onclick="() => ViewModel.MarkAllAsReadCommand.ExecuteAsync(null)" style="padding:12px 0;border-bottom:1px solid #eee;">
        ✓ Mark as Read
    </div>
    <div @onclick="() => ViewModel.UndoCommand.ExecuteAsync(null)" style="padding:12px 0;border-bottom:1px solid #eee;">
        ↩ Undo
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/settings"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        ⚙ Message Settings
    </div>
</div>
```

(This reorders rows to match the spec's final intended order — Theme, [Your Profile lands in Task 8], Trash, Blocked, Mark as Read, Undo, Message Settings.)

- [ ] **Step 12: Register DI**

In `src/SmsMessenger/MauiProgram.cs`, after the `IMarkAsReadService` line added in Task 6, add:

```csharp
builder.Services.AddSingleton<IThemeService, ThemeService>();
builder.Services.AddTransient<ThemeViewModel>();
```

- [ ] **Step 13: Build to verify it compiles**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 14: Manual verification on the physical device**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -t:Run -f net9.0-android
```

On the device: open the menu → Theme. Tap "Dark" — confirm the background of every screen turns dark and stays dark navigating around the app. Tap a different accent swatch — confirm the Splash screen and the Compose FAB both pick up the new color the next time they're visible. Fully close the app (swipe it away) and relaunch — confirm it comes back up in the theme/accent you left it in, not the default.

- [ ] **Step 15: Commit**

```bash
git add src/SmsMessenger.Core/Models/ThemeMode.cs src/SmsMessenger.Core/Services/IThemeService.cs src/SmsMessenger/Platforms/Android/ThemeService.cs src/SmsMessenger.Core/ViewModels/ThemeViewModel.cs tests/SmsMessenger.Core.Tests/ViewModels/ThemeViewModelTests.cs src/SmsMessenger/wwwroot/js/theme.js src/SmsMessenger/wwwroot/index.html src/SmsMessenger/wwwroot/css/app.css src/SmsMessenger/Pages/Theme src/SmsMessenger/Pages/SplashPage.razor src/SmsMessenger/Pages/Conversations/ConversationsPage.razor src/SmsMessenger/Pages/Menu/MenuPage.razor src/SmsMessenger/MauiProgram.cs
git commit -m "feat: add Theme (light/dark/system + accent color)"
```

---

### Task 8: Your Profile

**Files:**
- Create: `src/SmsMessenger.Core/Models/UserProfile.cs`
- Create: `src/SmsMessenger.Core/Services/IProfileService.cs`
- Create: `src/SmsMessenger/Platforms/Android/ProfileService.cs`
- Create: `src/SmsMessenger.Core/ViewModels/ProfileViewModel.cs`
- Test: `tests/SmsMessenger.Core.Tests/ViewModels/ProfileViewModelTests.cs`
- Create: `src/SmsMessenger/Pages/Profile/ProfilePage.razor`
- Modify: `src/SmsMessenger/Pages/Menu/MenuPage.razor` (add the profile header card and the "Your Profile" row)
- Modify: `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor` (top-right icon shows the profile photo when set, gear otherwise)
- Modify: `src/SmsMessenger/MauiProgram.cs` (register `IProfileService`, `ProfileViewModel`)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `IProfileService` (`GetProfile(): UserProfile`, `SaveProfile(string displayName, string? photoPath)`, `PickPhotoAsync(): Task<string?>` — returns the saved local path, or `null` if the user cancelled). `ProfileViewModel` (`DisplayName: string`, `PhotoPath: string?`, `PickPhotoCommand`, `SaveCommand`) is consumed by `ProfilePage.razor`. This is the last new service this plan introduces — `MenuPage` and `ConversationsPage` both consume `IProfileService.GetProfile()` directly (a plain synchronous read, no view model needed for that) to decide what to show in their respective headers/icons.

- [ ] **Step 1: Write the failing tests for `ProfileViewModel`**

`tests/SmsMessenger.Core.Tests/ViewModels/ProfileViewModelTests.cs`:

```csharp
using Moq;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ProfileViewModelTests
{
    [Fact]
    public void Constructor_loads_the_currently_saved_profile()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "Travis", PhotoPath = "/path/photo.jpg" });

        var viewModel = new ProfileViewModel(profileService.Object);

        Assert.Equal("Travis", viewModel.DisplayName);
        Assert.Equal("/path/photo.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public async Task PickPhotoCommand_updates_PhotoPath_when_a_photo_is_picked()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = null });
        profileService.Setup(s => s.PickPhotoAsync()).ReturnsAsync("/path/new.jpg");
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal("/path/new.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public async Task PickPhotoCommand_leaves_PhotoPath_unchanged_when_cancelled()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = "/path/old.jpg" });
        profileService.Setup(s => s.PickPhotoAsync()).ReturnsAsync((string?)null);
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal("/path/old.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public void SaveCommand_saves_the_current_name_and_photo()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = null });
        var viewModel = new ProfileViewModel(profileService.Object) { DisplayName = "Travis", PhotoPath = "/path/photo.jpg" };

        viewModel.SaveCommand.Execute(null);

        profileService.Verify(s => s.SaveProfile("Travis", "/path/photo.jpg"), Times.Once);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter ProfileViewModelTests
```

Expected: FAIL — `UserProfile`, `IProfileService`, and `ProfileViewModel` don't exist yet.

- [ ] **Step 3: Implement `UserProfile`, `IProfileService`, and `ProfileViewModel`**

`src/SmsMessenger.Core/Models/UserProfile.cs`:

```csharp
namespace SmsMessenger.Core.Models;

public class UserProfile
{
    public required string DisplayName { get; init; }
    public string? PhotoPath { get; init; }
}
```

`src/SmsMessenger.Core/Services/IProfileService.cs`:

```csharp
using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Services;

public interface IProfileService
{
    UserProfile GetProfile();
    void SaveProfile(string displayName, string? photoPath);
    Task<string?> PickPhotoAsync();
}
```

`src/SmsMessenger.Core/ViewModels/ProfileViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly IProfileService _profileService;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _photoPath;

    public ProfileViewModel(IProfileService profileService)
    {
        _profileService = profileService;
        var profile = _profileService.GetProfile();
        _displayName = profile.DisplayName;
        _photoPath = profile.PhotoPath;
    }

    [RelayCommand]
    private async Task PickPhoto()
    {
        var path = await _profileService.PickPhotoAsync();
        if (path is not null)
        {
            PhotoPath = path;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _profileService.SaveProfile(DisplayName, PhotoPath);
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test tests/SmsMessenger.Core.Tests --filter ProfileViewModelTests
```

Expected: PASS (4 tests).

- [ ] **Step 5: Implement the Android `ProfileService`**

`src/SmsMessenger/Platforms/Android/ProfileService.cs`:

```csharp
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Platforms.Android;

public class ProfileService : IProfileService
{
    private const string NameKey = "profile_display_name";
    private const string PhotoKey = "profile_photo_path";

    public UserProfile GetProfile()
    {
        var photoPath = Preferences.Get(PhotoKey, string.Empty);
        return new UserProfile
        {
            DisplayName = Preferences.Get(NameKey, string.Empty),
            PhotoPath = string.IsNullOrEmpty(photoPath) ? null : photoPath
        };
    }

    public void SaveProfile(string displayName, string? photoPath)
    {
        Preferences.Set(NameKey, displayName);
        if (photoPath is not null)
        {
            Preferences.Set(PhotoKey, photoPath);
        }
    }

    public async Task<string?> PickPhotoAsync()
    {
        var result = await MediaPicker.Default.PickPhotoAsync();
        if (result is null)
        {
            return null;
        }

        var destinationPath = Path.Combine(FileSystem.AppDataDirectory, "profile_photo" + Path.GetExtension(result.FileName));
        using var sourceStream = await result.OpenReadAsync();
        using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream);

        return destinationPath;
    }
}
```

(`MediaPicker.Default.PickPhotoAsync()` uses Android's modern Photo Picker on this device's API level — no new runtime permission needed. The picked file is copied into `FileSystem.AppDataDirectory` because the picker's own temp file is not guaranteed to survive after the picker returns.)

- [ ] **Step 6: Create the Profile page**

`src/SmsMessenger/Pages/Profile/ProfilePage.razor`:

```razor
@page "/profile"
@inject SmsMessenger.Core.ViewModels.ProfileViewModel ViewModel
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    <h3>Your Profile</h3>

    @if (!string.IsNullOrEmpty(ViewModel.PhotoPath))
    {
        <img src="@($"file://{ViewModel.PhotoPath}")"
             @onclick="() => ViewModel.PickPhotoCommand.ExecuteAsync(null)"
             style="width:80px;height:80px;border-radius:50%;object-fit:cover;" />
    }
    else
    {
        <button @onclick="() => ViewModel.PickPhotoCommand.ExecuteAsync(null)"
                style="width:80px;height:80px;border-radius:50%;background:#e2e8f0;border:none;">
            + Photo
        </button>
    }

    <div style="margin-top:12px;">
        <input placeholder="Your name" @bind="ViewModel.DisplayName" />
    </div>

    <button @onclick="Save" style="margin-top:12px;">Save</button>
</div>

@code {
    private void Save()
    {
        ViewModel.SaveCommand.Execute(null);
        Nav.NavigateTo("/menu");
    }
}
```

- [ ] **Step 7: Add the profile header and "Your Profile" row to the Menu page**

Replace the full contents of `src/SmsMessenger/Pages/Menu/MenuPage.razor`:

```razor
@page "/menu"
@inject SmsMessenger.Core.ViewModels.MenuViewModel ViewModel
@inject SmsMessenger.Core.Services.IProfileService ProfileService
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    @{ var profile = ProfileService.GetProfile(); }
    <div @onclick="@(() => Nav.NavigateTo("/profile"))"
         style="display:flex;align-items:center;gap:12px;padding:12px;background:#f8fafc;border-radius:12px;margin-bottom:12px;">
        @if (!string.IsNullOrEmpty(profile.PhotoPath))
        {
            <img src="@($"file://{profile.PhotoPath}")" style="width:56px;height:56px;border-radius:50%;object-fit:cover;" />
        }
        else
        {
            <div style="width:56px;height:56px;border-radius:50%;background:#e2e8f0;"></div>
        }
        <strong>@(string.IsNullOrEmpty(profile.DisplayName) ? "Set up your profile" : profile.DisplayName)</strong>
    </div>

    @if (!string.IsNullOrEmpty(ViewModel.StatusMessage))
    {
        <p style="color:#06b6d4;">@ViewModel.StatusMessage</p>
    }

    <div @onclick="@(() => Nav.NavigateTo("/theme"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🎨 Theme
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/profile"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        👤 Your Profile
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/trash"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🗑 Trash
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/blocked"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🚫 Blocked
    </div>
    <div @onclick="() => ViewModel.MarkAllAsReadCommand.ExecuteAsync(null)" style="padding:12px 0;border-bottom:1px solid #eee;">
        ✓ Mark as Read
    </div>
    <div @onclick="() => ViewModel.UndoCommand.ExecuteAsync(null)" style="padding:12px 0;border-bottom:1px solid #eee;">
        ↩ Undo
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/settings"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        ⚙ Message Settings
    </div>
</div>
```

- [ ] **Step 8: Swap the Conversations top-right icon between the profile photo and the gear**

Replace the full contents of `src/SmsMessenger/Pages/Conversations/ConversationsPage.razor`:

```razor
@page "/conversations"
@using Microsoft.AspNetCore.Components
@inject SmsMessenger.Core.ViewModels.ConversationsViewModel ViewModel
@inject SmsMessenger.Core.Services.IProfileService ProfileService
@inject NavigationManager Nav

<div style="padding:12px;">
    <div style="display:flex;justify-content:flex-end;margin-bottom:8px;">
        @{ var profile = ProfileService.GetProfile(); }
        @if (!string.IsNullOrEmpty(profile.PhotoPath))
        {
            <img @onclick="@(() => Nav.NavigateTo("/menu"))" src="@($"file://{profile.PhotoPath}")"
                 style="width:40px;height:40px;border-radius:50%;object-fit:cover;" />
        }
        else
        {
            <button @onclick="@(() => Nav.NavigateTo("/menu"))"
                    style="width:40px;height:40px;border-radius:50%;border:none;background:#e2e8f0;font-size:1.2em;">
                ⚙
            </button>
        }
    </div>

    <input placeholder="Search contacts or numbers" @bind="ViewModel.SearchText" @bind:event="oninput" />

    @if (ViewModel.Threads.Count == 0)
    {
        <p>No conversations yet.</p>
    }
    else
    {
        @foreach (var thread in ViewModel.Threads)
        {
            <div @onclick="() => OpenThread(thread.Id, thread.Address)"
                 @onpointerdown="() => OnPointerDown(thread.Id, thread.Address)"
                 @onpointerup="() => OnPointerUp(thread.Id, thread.Address)"
                 @onpointerleave="CancelLongPress"
                 style="padding:10px;border-bottom:1px solid #eee;">
                <strong>@thread.DisplayNameOrAddress</strong>
                @if (thread.UnreadCount > 0)
                {
                    <span style="color:#06b6d4;"> ● @thread.UnreadCount</span>
                }
                <div style="color:#94a3b8;font-size:0.85em;">@thread.PreviewText</div>
            </div>
        }
    }

    <button @onclick="@(() => Nav.NavigateTo("/compose"))"
            style="position:fixed;right:24px;bottom:24px;width:56px;height:56px;border-radius:50%;background:var(--accent-color);color:white;font-size:1.5em;border:none;">
        +
    </button>
</div>

@if (_actionSheetThreadId is not null)
{
    <div style="position:fixed;inset:0;background:rgba(0,0,0,0.4);display:flex;align-items:flex-end;" @onclick="CloseActionSheet">
        <div style="background:white;width:100%;padding:12px;" @onclick:stopPropagation="true">
            <div @onclick="TrashSelectedThread" style="padding:12px;">🗑 Trash</div>
            <div @onclick="BlockSelectedThread" style="padding:12px;">🚫 Block</div>
        </div>
    </div>
}

@code {
    private long? _actionSheetThreadId;
    private string _actionSheetAddress = string.Empty;
    private CancellationTokenSource? _longPressCts;
    private bool _longPressTriggered;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OpenThread(long threadId, string address)
    {
        Nav.NavigateTo($"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}");
    }

    private void OnPointerDown(long threadId, string address)
    {
        _longPressTriggered = false;
        _longPressCts = new CancellationTokenSource();
        var token = _longPressCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                _longPressTriggered = true;
                await InvokeAsync(() =>
                {
                    _actionSheetThreadId = threadId;
                    _actionSheetAddress = address;
                    StateHasChanged();
                });
            }
            catch (TaskCanceledException)
            {
                // pointer released or left before the long-press threshold — a normal tap, not a long-press
            }
        });
    }

    private void CancelLongPress() => _longPressCts?.Cancel();

    private void OnPointerUp(long threadId, string address)
    {
        _longPressCts?.Cancel();
        if (!_longPressTriggered)
        {
            OpenThread(threadId, address);
        }
    }

    private void CloseActionSheet() => _actionSheetThreadId = null;

    private async Task TrashSelectedThread()
    {
        if (_actionSheetThreadId is long id)
        {
            await ViewModel.TrashThreadCommand.ExecuteAsync(id);
        }
        CloseActionSheet();
    }

    private async Task BlockSelectedThread()
    {
        if (_actionSheetThreadId is not null)
        {
            await ViewModel.BlockThreadCommand.ExecuteAsync(_actionSheetAddress);
        }
        CloseActionSheet();
    }
}
```

- [ ] **Step 9: Register DI**

In `src/SmsMessenger/MauiProgram.cs`, after the `IThemeService` line added in Task 7, add:

```csharp
builder.Services.AddSingleton<IProfileService, ProfileService>();
builder.Services.AddTransient<ProfileViewModel>();
```

- [ ] **Step 10: Build to verify it compiles**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 11: Manual verification on the physical device**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -t:Run -f net9.0-android
```

On the device: confirm the Conversations top-right icon is still the gear (no profile set yet). Tap it → menu → tap the header card ("Set up your profile") → confirm it opens `/profile`. Type a name, tap "+ Photo," pick one from the device's photo picker, confirm it shows in the circle, tap Save. Confirm you're back on the menu with the header now showing that name and photo. Go back to Conversations — confirm the top-right icon is now that same photo instead of the gear.

- [ ] **Step 12: Commit**

```bash
git add src/SmsMessenger.Core/Models/UserProfile.cs src/SmsMessenger.Core/Services/IProfileService.cs src/SmsMessenger/Platforms/Android/ProfileService.cs src/SmsMessenger.Core/ViewModels/ProfileViewModel.cs tests/SmsMessenger.Core.Tests/ViewModels/ProfileViewModelTests.cs src/SmsMessenger/Pages/Profile src/SmsMessenger/Pages/Menu/MenuPage.razor src/SmsMessenger/Pages/Conversations/ConversationsPage.razor src/SmsMessenger/MauiProgram.cs
git commit -m "feat: add Your Profile and show it as the Conversations top-right icon"
```

---

### Task 9: End-to-end walkthrough and README update

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: every page and service from Tasks 1–8.
- Produces: nothing new — this is the acceptance pass for the whole plan and the documentation update for whoever picks the repo up next.

- [ ] **Step 1: Full manual walkthrough on the physical device**

```bash
dotnet build src/SmsMessenger/SmsMessenger.csproj -t:Run -f net9.0-android
```

Walk through, in order, on the device: Conversations shows the gear (or your profile photo, if Task 8 was tested with one already set) top-right, no bottom nav bar anywhere → long-press a conversation, Trash it, confirm it vanishes → menu → Trash, confirm it's listed, Restore it, confirm it's back in Conversations → long-press a different conversation, Block it, confirm it vanishes → menu → Blocked, confirm it's listed, Unblock it → menu → Mark as Read, confirm the status line and that unread dots clear → menu → Undo, confirm it reverses that same mark-as-read → menu → Theme, switch to Dark and a different accent, confirm the whole app re-colors and a full app restart keeps the choice → menu → Your Profile, set a name and photo, Save, confirm the menu header and the Conversations top-right icon both reflect it. If any step doesn't work, that's a defect in the corresponding earlier task, not a new task.

- [ ] **Step 2: Run the full unit test suite one more time**

```bash
dotnet test tests/SmsMessenger.Core.Tests
```

Expected: PASS, all tests (36 from the original plan + everything added in Tasks 1–8 of this plan).

- [ ] **Step 3: Update the README**

Add a new section to `README.md`, right after the existing "## What this app does and doesn't do" section:

```markdown

## The menu (top-right icon on Conversations)

Tapping the top-right icon on Conversations (your profile photo once you've
set one, a gear icon until then) opens a menu with:

- **Theme** — Light/Dark/System plus an accent color, applied instantly and
  remembered across restarts.
- **Your Profile** — a display name and photo, shown in the menu header and
  as the Conversations top-right icon.
- **Trash** — long-press a conversation in Conversations to trash it (the
  underlying messages are never deleted from Android's SMS provider, only
  hidden and recorded in a local SQLite table); restore it from here.
- **Blocked** — long-press a conversation to block it, which both hides it
  locally and registers the number with Android's `BlockedNumberContract`
  so the OS stops delivering future texts from it; unblock from here.
- **Mark as Read** — marks every conversation read in one action.
- **Undo** — reverses the single most recent Trash, Block, or Mark as Read
  action; press again to reverse the one before that. The undo stack is
  in-memory only and resets when the app restarts.

See `docs/superpowers/specs/2026-09-18-conversations-menu-design.md` and
`docs/superpowers/plans/2026-09-18-conversations-menu.md` for the design and
implementation plan this was built from.
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: document the conversations menu, theme, profile, trash, blocked, and undo"
```

---

## Plan Self-Review

- **Spec coverage:** Navigation shell (top-right icon, `/menu`, bottom nav removal) → Task 3. Message Settings → Task 3 (re-linked, zero new logic, exactly as the spec's Out of Scope/Message Settings section calls for). Theme (Light/Dark/System + accent, applied instantly, remembered) → Task 7. Your Profile (name + photo, shown in both the menu header and the Conversations icon) → Task 8. Trash (long-press trigger, never deletes from Android's provider, restore) → Task 4. Blocked (long-press trigger, `BlockedNumberContract` OS-level integration with a documented fallback, unblock) → Task 5. Mark as Read (global) → Task 6. Undo (in-memory stack, covers exactly Trash/Block/Mark-as-read, LIFO, re-pressable) → Task 2 (mechanism) + Task 6 (menu wiring) + Tasks 4/5/6 (each pushes its own action). The spec's data-layer section (two SQLite tables, everything else in Preferences, no new `IPreferencesService` abstraction) → Task 1 for the tables, and Tasks 7/8 deliberately read/write `Preferences` directly with no wrapper, matching the spec's explicit call. The spec's "Out of scope" list (Archived, Device pairing, Your data in Messages, Help & feedback, per-message trash, per-thread mark-as-read, undoing sent/received messages) has no corresponding task anywhere in this plan, correctly.
- **Placeholder scan:** No "TBD"/"handle appropriately" phrasing found. The one place this plan explicitly asks the implementer to adjust a detail (Task 5's `BlockedNumberContract` member names) gives a concrete fallback and names exactly which two lines to change, matching how the original plan handled its own `Permissions.PostNotifications` uncertainty in Task 6 — not an open placeholder.
- **Type consistency:** `ITrashRepository`/`IBlockedNumberRepository` signatures (Task 1) match every caller in Tasks 4/5/6 exactly. `ConversationsViewModel`'s constructor grows from `(IThreadService)` (original plan) → `(IThreadService, ITrashRepository, IUndoStack)` (Task 4) → `(IThreadService, ITrashRepository, IContactBlockService, IUndoStack)` (Task 5) and stays at that final shape for the rest of the plan; every task after Task 5 that references it uses that exact four-argument order. `IUndoableAction`/`IUndoStack` (Task 2) are implemented identically by `TrashUndoAction` (Task 4), `BlockUndoAction` (Task 5), and `MarkAsReadUndoAction` (Task 6), and consumed identically by `MenuViewModel.UndoCommand` (Task 6). `IProfileService.GetProfile()`'s `UserProfile` shape (Task 8) is used identically by `ProfileViewModel`, `MenuPage.razor`, and `ConversationsPage.razor`.

