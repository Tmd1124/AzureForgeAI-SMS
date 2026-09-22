# Conversation Filters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add user-defined, multi-assign conversation filters (labels): create/rename/recolor/delete filters on a new maintenance page, assign multiple filters to conversations from the existing multi-select toolbar, and browse the conversation list narrowed to one or more filters from a new header icon.

**Architecture:** A new SQLite-backed `IFilterRepository` (same pattern as `IFavoriteRepository`/`IArchiveRepository`) adds two tables to the existing `ForgeLinkSms.db`. `ConversationsViewModel` gains a `Filters` collection and `ActiveFilterIds` set, feeding into its existing `ApplyFilter()` narrowing chain. A new shared `FilterPickerSheet.razor` bottom-sheet component (mirroring `AttachmentDrawer.razor`'s `IsOpen`/`Open()`/`Close()` shape) is reused for both browsing (header icon) and assigning (multi-select toolbar icon). A new `/filters` page (`FiltersViewModel` + `FiltersPage.razor`) handles CRUD on filter definitions, reached from `MenuPage.razor` and from the picker sheet.

**Tech Stack:** .NET MAUI Blazor Hybrid, CommunityToolkit.Mvvm (`[ObservableObject]`/`[RelayCommand]`/`[ObservableProperty]`), sqlite-net-pcl, xUnit + Moq for `ForgeLinkSms.Core.Tests`.

**Spec:** `docs/superpowers/specs/2026-09-22-conversation-filters-design.md`

## Global Constraints

- Repository classes follow the exact existing shape: `SQLiteAsyncConnection` field, constructor takes `string databasePath`, `InitializeAsync()` creates tables, implements `IDisposable` with `_db.CloseAsync().GetAwaiter().GetResult()`.
- ViewModels use CommunityToolkit.Mvvm source generators (`[ObservableObject]` partial class, `[RelayCommand]` on private methods, `[ObservableProperty]` on private fields), matching every existing `*ViewModel` in `ForgeLinkSms.Core/ViewModels`.
- Razor markup uses plain inline `style="..."` attributes only — no CSS frameworks, no `.razor.css` isolation files, matching every existing page in this codebase.
- No confirmation dialogs for destructive actions on the maintenance page (delete is immediate), matching `BlockedPage.razor`'s Unblock and `ArchivedPage.razor`'s Unarchive one-tap convention.
- Preset filter colors, exact 8 hex values in this exact order: `#ef4444`, `#f97316`, `#eab308`, `#22c55e`, `#06b6d4`, `#6366f1`, `#a855f7`, `#ec4899`.
- Tests: xUnit (`[Fact]`, global `using Xunit;` already configured) + Moq (`using Moq;` per file), mirroring the exact structure of `FavoriteRepositoryTests.cs` / `ConversationsViewModelTests.cs` / `ArchivedViewModelTests.cs`.
- No `.razor` component test harness exists in this repo (no bUnit) — UI tasks are verified by building, deploying to the connected device, and manually exercising the feature, matching this project's established pattern for every prior UI change this session.

**Design note:** the spec calls for deleting a filter to also clear it from `ActiveFilterIds` so browsing can't get stuck on a filter that no longer exists. No explicit code implements that anywhere in this plan — `ConversationsViewModel` is registered `AddTransient` (see `MauiProgram.cs`), so a fresh instance with an empty `ActiveFilterIds` is constructed every time `ConversationsPage` is navigated to, and `/filters` is a separate route. Deleting a filter there and navigating back always re-runs `Load()` on a brand-new `ConversationsViewModel`, which satisfies the requirement without extra plumbing.

---

### Task 1: Filter data model and repository

**Files:**
- Create: `src/ForgeLinkSms.Core/Models/Filter.cs`
- Create: `src/ForgeLinkSms.Core/Models/ThreadFilterAssignment.cs`
- Create: `src/ForgeLinkSms.Core/Data/IFilterRepository.cs`
- Create: `src/ForgeLinkSms.Core/Data/FilterRepository.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Data/FilterRepositoryTests.cs`
- Modify: `src/ForgeLinkSms/MauiProgram.cs:57-60` (add the repository registration right after the existing `archiveRepository` block)

**Interfaces:**
- Produces: `ForgeLinkSms.Core.Models.Filter` (`Id: long`, `Name: string`, `ColorHex: string`, all mutable), `ForgeLinkSms.Core.Models.ThreadFilterAssignment` (`Id: long`, `ThreadId: long`, `FilterId: long`), `ForgeLinkSms.Core.Data.IFilterRepository` with methods `InitializeAsync()`, `GetAllFiltersAsync() : Task<IReadOnlyList<Filter>>`, `CreateFilterAsync(string name, string colorHex) : Task<Filter>`, `RenameFilterAsync(long filterId, string newName) : Task`, `SetFilterColorAsync(long filterId, string colorHex) : Task`, `DeleteFilterAsync(long filterId) : Task`, `GetAllAssignmentsAsync() : Task<IReadOnlyDictionary<long, List<long>>>`, `AssignFilterAsync(long threadId, long filterId) : Task`, `UnassignFilterAsync(long threadId, long filterId) : Task`.

- [ ] **Step 1: Write the model files**

`src/ForgeLinkSms.Core/Models/Filter.cs`:

```csharp
using SQLite;

namespace ForgeLinkSms.Core.Models;

public class Filter
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;
}
```

`src/ForgeLinkSms.Core/Models/ThreadFilterAssignment.cs`:

```csharp
using SQLite;

namespace ForgeLinkSms.Core.Models;

public class ThreadFilterAssignment
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public long ThreadId { get; set; }

    public long FilterId { get; set; }
}
```

- [ ] **Step 2: Write `IFilterRepository`**

`src/ForgeLinkSms.Core/Data/IFilterRepository.cs`:

```csharp
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public interface IFilterRepository
{
    Task InitializeAsync();
    Task<IReadOnlyList<Filter>> GetAllFiltersAsync();
    Task<Filter> CreateFilterAsync(string name, string colorHex);
    Task RenameFilterAsync(long filterId, string newName);
    Task SetFilterColorAsync(long filterId, string colorHex);
    Task DeleteFilterAsync(long filterId);
    Task<IReadOnlyDictionary<long, List<long>>> GetAllAssignmentsAsync();
    Task AssignFilterAsync(long threadId, long filterId);
    Task UnassignFilterAsync(long threadId, long filterId);
}
```

- [ ] **Step 3: Write the failing tests**

`tests/ForgeLinkSms.Core.Tests/Data/FilterRepositoryTests.cs`:

```csharp
using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Tests.Data;

public class FilterRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FilterRepository _repository;

    public FilterRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"filter-test-{Guid.NewGuid()}.db3");
        _repository = new FilterRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task GetAllFiltersAsync_is_empty_initially()
    {
        var filters = await _repository.GetAllFiltersAsync();

        Assert.Empty(filters);
    }

    [Fact]
    public async Task CreateFilterAsync_returns_the_filter_with_an_assigned_id()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        Assert.True(filter.Id > 0);
        Assert.Equal("Work", filter.Name);
        Assert.Equal("#6366f1", filter.ColorHex);

        var all = await _repository.GetAllFiltersAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task RenameFilterAsync_updates_the_name()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.RenameFilterAsync(filter.Id, "Personal");

        var all = await _repository.GetAllFiltersAsync();
        Assert.Equal("Personal", all[0].Name);
    }

    [Fact]
    public async Task SetFilterColorAsync_updates_the_color()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.SetFilterColorAsync(filter.Id, "#ef4444");

        var all = await _repository.GetAllFiltersAsync();
        Assert.Equal("#ef4444", all[0].ColorHex);
    }

    [Fact]
    public async Task DeleteFilterAsync_removes_the_filter()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.DeleteFilterAsync(filter.Id);

        Assert.Empty(await _repository.GetAllFiltersAsync());
    }

    [Fact]
    public async Task DeleteFilterAsync_also_removes_its_assignments()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");
        await _repository.AssignFilterAsync(threadId: 1, filterId: filter.Id);

        await _repository.DeleteFilterAsync(filter.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.False(assignments.ContainsKey(1));
    }

    [Fact]
    public async Task AssignFilterAsync_then_GetAllAssignmentsAsync_reflects_it()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.AssignFilterAsync(threadId: 42, filterId: filter.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.True(assignments.ContainsKey(42));
        Assert.Equal(new List<long> { filter.Id }, assignments[42]);
    }

    [Fact]
    public async Task AssignFilterAsync_twice_does_not_create_a_duplicate_assignment()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");

        await _repository.AssignFilterAsync(threadId: 42, filterId: filter.Id);
        await _repository.AssignFilterAsync(threadId: 42, filterId: filter.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.Single(assignments[42]);
    }

    [Fact]
    public async Task UnassignFilterAsync_removes_only_that_threads_assignment()
    {
        var filter = await _repository.CreateFilterAsync("Work", "#6366f1");
        await _repository.AssignFilterAsync(threadId: 1, filterId: filter.Id);
        await _repository.AssignFilterAsync(threadId: 2, filterId: filter.Id);

        await _repository.UnassignFilterAsync(threadId: 1, filterId: filter.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();
        Assert.False(assignments.ContainsKey(1));
        Assert.True(assignments.ContainsKey(2));
    }

    [Fact]
    public async Task GetAllAssignmentsAsync_groups_multiple_filters_on_the_same_thread()
    {
        var work = await _repository.CreateFilterAsync("Work", "#6366f1");
        var urgent = await _repository.CreateFilterAsync("Urgent", "#ef4444");
        await _repository.AssignFilterAsync(threadId: 1, filterId: work.Id);
        await _repository.AssignFilterAsync(threadId: 1, filterId: urgent.Id);

        var assignments = await _repository.GetAllAssignmentsAsync();

        Assert.Equal(2, assignments[1].Count);
        Assert.Contains(work.Id, assignments[1]);
        Assert.Contains(urgent.Id, assignments[1]);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj --filter FilterRepositoryTests`
Expected: FAIL to build — `FilterRepository` doesn't exist yet.

- [ ] **Step 5: Write `FilterRepository`**

`src/ForgeLinkSms.Core/Data/FilterRepository.cs`:

```csharp
using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class FilterRepository : IFilterRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public FilterRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public async Task InitializeAsync()
    {
        await _db.CreateTableAsync<Filter>();
        await _db.CreateTableAsync<ThreadFilterAssignment>();
    }

    public async Task<IReadOnlyList<Filter>> GetAllFiltersAsync() =>
        await _db.Table<Filter>().ToListAsync();

    public async Task<Filter> CreateFilterAsync(string name, string colorHex)
    {
        var filter = new Filter { Name = name, ColorHex = colorHex };
        await _db.InsertAsync(filter);
        return filter;
    }

    public Task RenameFilterAsync(long filterId, string newName) =>
        _db.ExecuteAsync("UPDATE Filter SET Name = ? WHERE Id = ?", newName, filterId);

    public Task SetFilterColorAsync(long filterId, string colorHex) =>
        _db.ExecuteAsync("UPDATE Filter SET ColorHex = ? WHERE Id = ?", colorHex, filterId);

    public async Task DeleteFilterAsync(long filterId)
    {
        await _db.ExecuteAsync("DELETE FROM ThreadFilterAssignment WHERE FilterId = ?", filterId);
        await _db.DeleteAsync<Filter>(filterId);
    }

    public async Task<IReadOnlyDictionary<long, List<long>>> GetAllAssignmentsAsync()
    {
        var rows = await _db.Table<ThreadFilterAssignment>().ToListAsync();
        return rows
            .GroupBy(r => r.ThreadId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.FilterId).ToList());
    }

    public async Task AssignFilterAsync(long threadId, long filterId)
    {
        var existing = await _db.Table<ThreadFilterAssignment>()
            .Where(a => a.ThreadId == threadId && a.FilterId == filterId)
            .FirstOrDefaultAsync();
        if (existing is null)
        {
            await _db.InsertAsync(new ThreadFilterAssignment { ThreadId = threadId, FilterId = filterId });
        }
    }

    public Task UnassignFilterAsync(long threadId, long filterId) =>
        _db.ExecuteAsync("DELETE FROM ThreadFilterAssignment WHERE ThreadId = ? AND FilterId = ?", threadId, filterId);

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj --filter FilterRepositoryTests`
Expected: PASS, all 10 tests green.

- [ ] **Step 7: Register the repository in `MauiProgram.cs`**

In `src/ForgeLinkSms/MauiProgram.cs`, immediately after the existing block that ends `builder.Services.AddTransient<ArchivedViewModel>();` (currently line 60), insert:

```csharp
		var filterRepository = new FilterRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		filterRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IFilterRepository>(filterRepository);
```

(Note: this file uses tab indentation, not spaces — match the surrounding lines exactly.)

- [ ] **Step 8: Commit**

```bash
git add src/ForgeLinkSms.Core/Models/Filter.cs src/ForgeLinkSms.Core/Models/ThreadFilterAssignment.cs src/ForgeLinkSms.Core/Data/IFilterRepository.cs src/ForgeLinkSms.Core/Data/FilterRepository.cs tests/ForgeLinkSms.Core.Tests/Data/FilterRepositoryTests.cs src/ForgeLinkSms/MauiProgram.cs
git commit -m "feat: add Filter/ThreadFilterAssignment data model and repository"
```

---

### Task 2: ConversationsViewModel multi-filter support

**Files:**
- Modify: `src/ForgeLinkSms.Core/Models/SmsThread.cs`
- Modify: `src/ForgeLinkSms.Core/ViewModels/ConversationsViewModel.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/ConversationsViewModelTests.cs`

**Interfaces:**
- Consumes: `IFilterRepository` from Task 1 (`GetAllFiltersAsync()`, `GetAllAssignmentsAsync()`, `AssignFilterAsync`, `UnassignFilterAsync`).
- Produces: `SmsThread.FilterIds : IReadOnlyList<long>` (mutable, defaults to empty); `ConversationsViewModel.Filters : ObservableCollection<Filter>`; `ConversationsViewModel.ActiveFilterIds : HashSet<long>`; `ConversationsViewModel.ToggleActiveFilter(long filterId) : void`; `ConversationsViewModel.ToggleFilterForThreadsCommand : IAsyncRelayCommand<(IReadOnlyList<long> ThreadIds, long FilterId)>`. Task 4 (ConversationsPage) consumes all four.

- [ ] **Step 1: Add `FilterIds` to `SmsThread`**

In `src/ForgeLinkSms.Core/Models/SmsThread.cs`, add this property right after `IsFavorite`:

```csharp
    public bool IsFavorite { get; set; }
    public IReadOnlyList<long> FilterIds { get; set; } = Array.Empty<long>();
```

- [ ] **Step 2: Write the failing tests**

In `tests/ForgeLinkSms.Core.Tests/ViewModels/ConversationsViewModelTests.cs`:

First, add `using ForgeLinkSms.Core.Models;` stays as-is (already imported). Update the `MakeViewModel` helper and add a `MakeEmptyFilterRepository` helper — replace the existing helper block (currently lines 21-64) with:

```csharp
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

    private static Mock<IFavoriteRepository> MakeEmptyFavoriteRepository()
    {
        var repository = new Mock<IFavoriteRepository>();
        repository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static Mock<IArchiveRepository> MakeEmptyArchiveRepository()
    {
        var repository = new Mock<IArchiveRepository>();
        repository.Setup(r => r.GetArchivedThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static Mock<IFilterRepository> MakeEmptyFilterRepository()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        repository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>());
        return repository;
    }

    private static ConversationsViewModel MakeViewModel(
        Mock<IThreadService> threadService,
        Mock<ITrashRepository>? trashRepository = null,
        Mock<IContactBlockService>? blockService = null,
        Mock<IFavoriteRepository>? favoriteRepository = null,
        Mock<IUndoStack>? undoStack = null,
        Mock<IMarkAsReadService>? markAsReadService = null,
        Mock<IArchiveRepository>? archiveRepository = null,
        Mock<IFilterRepository>? filterRepository = null) =>
        new(
            threadService.Object,
            (trashRepository ?? MakeEmptyTrashRepository()).Object,
            (blockService ?? MakeEmptyBlockService()).Object,
            (favoriteRepository ?? MakeEmptyFavoriteRepository()).Object,
            (undoStack ?? new Mock<IUndoStack>()).Object,
            (markAsReadService ?? new Mock<IMarkAsReadService>()).Object,
            (archiveRepository ?? MakeEmptyArchiveRepository()).Object,
            (filterRepository ?? MakeEmptyFilterRepository()).Object);
```

Then append these new test methods at the end of the class, before the final closing `}`:

```csharp
    [Fact]
    public async Task LoadCommand_populates_Filters_from_the_repository()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>());
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>());
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Filters);
        Assert.Equal("Work", viewModel.Filters[0].Name);
    }

    [Fact]
    public async Task LoadCommand_populates_each_threads_FilterIds_from_assignments()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10, 20 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        var thread1 = viewModel.Threads.Single(t => t.Id == 1);
        var thread2 = viewModel.Threads.Single(t => t.Id == 2);
        Assert.Equal(new List<long> { 10, 20 }, thread1.FilterIds);
        Assert.Empty(thread2.FilterIds);
    }

    [Fact]
    public async Task ToggleActiveFilter_narrows_Threads_to_matching_filter_and_toggling_again_clears_it()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.ToggleActiveFilter(10);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);

        viewModel.ToggleActiveFilter(10);

        Assert.Equal(2, viewModel.Threads.Count);
    }

    [Fact]
    public async Task ToggleActiveFilter_with_multiple_active_filters_matches_any_of_them()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey"),
            MakeThread(3, "5550149999", "Carol Lee", "yo")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 },
            [2] = new List<long> { 20 },
            [3] = new List<long> { 30 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.ToggleActiveFilter(10);
        viewModel.ToggleActiveFilter(20);

        Assert.Equal(2, viewModel.Threads.Count);
        Assert.Contains(viewModel.Threads, t => t.Id == 1);
        Assert.Contains(viewModel.Threads, t => t.Id == 2);
    }

    [Fact]
    public async Task ActiveFilterIds_stacks_with_ShowUnreadOnly()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 0),
            MakeThread(2, "5550148890", "Bob Jones", "hey", unreadCount: 3)
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 },
            [2] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ToggleActiveFilter(10);

        viewModel.ShowUnreadOnly = true;

        Assert.Single(viewModel.Threads);
        Assert.Equal(2, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task ToggleFilterForThreadsCommand_assigns_the_filter_to_every_thread_missing_it()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ToggleFilterForThreadsCommand.ExecuteAsync((new List<long> { 1, 2 }, 10L));

        filterRepository.Verify(r => r.AssignFilterAsync(2, 10), Times.Once);
        filterRepository.Verify(r => r.AssignFilterAsync(1, 10), Times.Never);
        filterRepository.Verify(r => r.UnassignFilterAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
        Assert.Contains(10L, viewModel.Threads.Single(t => t.Id == 2).FilterIds);
    }

    [Fact]
    public async Task ToggleFilterForThreadsCommand_unassigns_the_filter_when_every_selected_thread_already_has_it()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var filterRepository = new Mock<IFilterRepository>();
        filterRepository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        filterRepository.Setup(r => r.GetAllAssignmentsAsync()).ReturnsAsync(new Dictionary<long, List<long>>
        {
            [1] = new List<long> { 10 },
            [2] = new List<long> { 10 }
        });
        var viewModel = MakeViewModel(threadService, filterRepository: filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ToggleFilterForThreadsCommand.ExecuteAsync((new List<long> { 1, 2 }, 10L));

        filterRepository.Verify(r => r.UnassignFilterAsync(1, 10), Times.Once);
        filterRepository.Verify(r => r.UnassignFilterAsync(2, 10), Times.Once);
        filterRepository.Verify(r => r.AssignFilterAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
        Assert.DoesNotContain(10L, viewModel.Threads.Single(t => t.Id == 1).FilterIds);
        Assert.DoesNotContain(10L, viewModel.Threads.Single(t => t.Id == 2).FilterIds);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj --filter ConversationsViewModelTests`
Expected: FAIL to build — `ConversationsViewModel` doesn't have a `Filters`/`ActiveFilterIds`/`ToggleActiveFilter`/`ToggleFilterForThreadsCommand` yet, and its constructor doesn't take an 8th `IFilterRepository` argument.

- [ ] **Step 4: Implement the `ConversationsViewModel` changes**

In `src/ForgeLinkSms.Core/ViewModels/ConversationsViewModel.cs`:

Add the field, right after `_archiveRepository` (line 19):

```csharp
    private readonly IArchiveRepository _archiveRepository;
    private readonly IFilterRepository _filterRepository;
```

Add the two new public collections, right after `public ObservableCollection<SmsThread> Threads { get; } = new();` (line 23):

```csharp
    public ObservableCollection<SmsThread> Threads { get; } = new();
    public ObservableCollection<Filter> Filters { get; } = new();
    public HashSet<long> ActiveFilterIds { get; } = new();
```

Replace the constructor (lines 33-49) with:

```csharp
    public ConversationsViewModel(
        IThreadService threadService,
        ITrashRepository trashRepository,
        IContactBlockService blockService,
        IFavoriteRepository favoriteRepository,
        IUndoStack undoStack,
        IMarkAsReadService markAsReadService,
        IArchiveRepository archiveRepository,
        IFilterRepository filterRepository)
    {
        _threadService = threadService;
        _trashRepository = trashRepository;
        _blockService = blockService;
        _favoriteRepository = favoriteRepository;
        _undoStack = undoStack;
        _markAsReadService = markAsReadService;
        _archiveRepository = archiveRepository;
        _filterRepository = filterRepository;
    }
```

Replace the body of `Load()` (lines 51-88) with:

```csharp
    [RelayCommand]
    private async Task Load()
    {
        // ExecuteAsync is called directly from several places (initial page load, every
        // trash/archive/favorite/read action, and an app-resume hook), bypassing the
        // AsyncRelayCommand's own CanExecute gate. Without this guard, two overlapping runs
        // both read-modify-write the shared _allThreads field and Threads collection with no
        // synchronization, which can silently corrupt the visible list.
        if (_isLoadingThreads)
        {
            return;
        }

        _isLoadingThreads = true;
        try
        {
            var threads = await _threadService.GetThreadsAsync();
            var trashedIds = await _trashRepository.GetTrashedThreadIdsAsync();
            var blockedNumbers = await _blockService.GetBlockedNumbersAsync();
            var favoriteIds = await _favoriteRepository.GetFavoriteThreadIdsAsync();
            var archivedIds = await _archiveRepository.GetArchivedThreadIdsAsync();
            var filters = await _filterRepository.GetAllFiltersAsync();
            var assignments = await _filterRepository.GetAllAssignmentsAsync();

            Filters.Clear();
            foreach (var filter in filters)
            {
                Filters.Add(filter);
            }

            _allThreads = threads
                .Where(t => !trashedIds.Contains(t.Id) && !blockedNumbers.Contains(t.Address) && !archivedIds.Contains(t.Id))
                .Select(t =>
                {
                    t.IsFavorite = favoriteIds.Contains(t.Id);
                    t.FilterIds = assignments.TryGetValue(t.Id, out var ids) ? ids : new List<long>();
                    return t;
                })
                .ToList();
            SortThreads();
            ApplyFilter();
        }
        finally
        {
            _isLoadingThreads = false;
        }
    }
```

Add these two new members right after `RemoveThreadsFromList` (after line 288, before `partial void OnSearchTextChanged`):

```csharp
    public void ToggleActiveFilter(long filterId)
    {
        if (!ActiveFilterIds.Remove(filterId))
        {
            ActiveFilterIds.Add(filterId);
        }
        ApplyFilter();
    }

    [RelayCommand]
    private async Task ToggleFilterForThreads((IReadOnlyList<long> ThreadIds, long FilterId) args)
    {
        var threads = args.ThreadIds
            .Select(id => _allThreads.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Cast<SmsThread>()
            .ToList();
        if (threads.Count == 0)
        {
            return;
        }

        // Mirrors FavoriteThreads: if every selected thread already has this filter, the
        // action removes it from all of them; otherwise it adds it to whichever ones are
        // missing it.
        var allHaveFilter = threads.All(t => t.FilterIds.Contains(args.FilterId));
        foreach (var thread in threads)
        {
            if (allHaveFilter)
            {
                await _filterRepository.UnassignFilterAsync(thread.Id, args.FilterId);
                thread.FilterIds = thread.FilterIds.Where(id => id != args.FilterId).ToList();
            }
            else if (!thread.FilterIds.Contains(args.FilterId))
            {
                await _filterRepository.AssignFilterAsync(thread.Id, args.FilterId);
                thread.FilterIds = thread.FilterIds.Append(args.FilterId).ToList();
            }
        }

        ApplyFilter();
    }
```

Finally, replace `ApplyFilter()` (lines 294-313) with:

```csharp
    private void ApplyFilter()
    {
        Threads.Clear();
        var query = SearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allThreads
            : _allThreads.Where(t =>
                t.DisplayNameOrAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.LastMessageBody.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (ShowUnreadOnly)
        {
            matches = matches.Where(t => t.UnreadCount > 0);
        }

        if (ActiveFilterIds.Count > 0)
        {
            matches = matches.Where(t => t.FilterIds.Any(id => ActiveFilterIds.Contains(id)));
        }

        foreach (var thread in matches)
        {
            Threads.Add(thread);
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj`
Expected: PASS — the full suite, including every pre-existing `ConversationsViewModelTests` case (the `MakeViewModel` helper change must not break them) and the new filter tests.

- [ ] **Step 6: Commit**

```bash
git add src/ForgeLinkSms.Core/Models/SmsThread.cs src/ForgeLinkSms.Core/ViewModels/ConversationsViewModel.cs tests/ForgeLinkSms.Core.Tests/ViewModels/ConversationsViewModelTests.cs
git commit -m "feat: add multi-filter support to ConversationsViewModel"
```

---

### Task 3: Shared `FilterPickerSheet` component

**Files:**
- Create: `src/ForgeLinkSms/Components/FilterPickerSheet.razor`

**Interfaces:**
- Consumes: `ForgeLinkSms.Core.Models.Filter` from Task 1.
- Produces: a component with public `bool IsOpen { get; }`, public `void Open()`, public `void Close()`, and parameters `Filters : IReadOnlyList<Filter>`, `IsChecked : Func<Filter, bool>`, `OnToggle : EventCallback<Filter>`, `ShowManageLink : bool`, `OnManageFilters : EventCallback`, `OnClosed : EventCallback`. Task 4 (ConversationsPage) consumes all of this by `@ref`.

- [ ] **Step 1: Write the component**

`src/ForgeLinkSms/Components/FilterPickerSheet.razor`:

```razor
@using ForgeLinkSms.Core.Models

@if (_isOpen)
{
    <div style="position:fixed;inset:0;background:rgba(0,0,0,0.4);display:flex;align-items:flex-end;z-index:20;" @onclick="Close">
        <div style="background:var(--card-color);width:100%;padding:12px;border-radius:16px 16px 0 0;max-height:60vh;overflow-y:auto;" @onclick:stopPropagation="true">
            @if (Filters.Count == 0)
            {
                <p style="text-align:center;color:#94a3b8;padding:12px;">No filters yet.</p>
            }
            else
            {
                @foreach (var filter in Filters)
                {
                    <div @onclick="() => OnToggle.InvokeAsync(filter)"
                         style="display:flex;align-items:center;gap:10px;padding:10px 0;border-bottom:1px solid #eee;cursor:pointer;">
                        <div style="width:20px;height:20px;border-radius:50%;background:@filter.ColorHex;flex-shrink:0;"></div>
                        <span style="flex:1;">@filter.Name</span>
                        <div style="width:20px;height:20px;border:2px solid var(--accent-color);border-radius:4px;display:flex;align-items:center;justify-content:center;background:@(IsChecked(filter) ? "var(--accent-color)" : "transparent");color:white;flex-shrink:0;">
                            @(IsChecked(filter) ? "✓" : "")
                        </div>
                    </div>
                }
            }
            @if (ShowManageLink)
            {
                <div @onclick="OnManageFilters" style="padding:12px 0 0;color:var(--accent-color);font-weight:600;cursor:pointer;">
                    Manage filters
                </div>
            }
        </div>
    </div>
}

@code {
    private bool _isOpen;

    public bool IsOpen => _isOpen;

    [Parameter, EditorRequired]
    public IReadOnlyList<Filter> Filters { get; set; } = Array.Empty<Filter>();

    [Parameter, EditorRequired]
    public Func<Filter, bool> IsChecked { get; set; } = _ => false;

    [Parameter]
    public EventCallback<Filter> OnToggle { get; set; }

    [Parameter]
    public bool ShowManageLink { get; set; }

    [Parameter]
    public EventCallback OnManageFilters { get; set; }

    [Parameter]
    public EventCallback OnClosed { get; set; }

    public void Open()
    {
        _isOpen = true;
        StateHasChanged();
    }

    public void Close()
    {
        _isOpen = false;
        StateHasChanged();
        _ = OnClosed.InvokeAsync();
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android -c Debug -v q -p:EmbedAssembliesIntoApk=true`
Expected: `Build succeeded.` — this component isn't referenced by any page yet, so there is nothing to manually verify until Task 4 wires it in.

- [ ] **Step 3: Commit**

```bash
git add src/ForgeLinkSms/Components/FilterPickerSheet.razor
git commit -m "feat: add shared FilterPickerSheet bottom-sheet component"
```

---

### Task 4: Wire filters into ConversationsPage

**Files:**
- Modify: `src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor`

**Interfaces:**
- Consumes: `ConversationsViewModel.Filters`, `.ActiveFilterIds`, `.ToggleActiveFilter(long)`, `.ToggleFilterForThreadsCommand` from Task 2; `FilterPickerSheet` (`IsOpen`, `Open()`, `Close()`, and its parameters) from Task 3.

- [ ] **Step 1: Add the funnel icon constant**

In `src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor`, in the `@code` block, add this right after the `EyeSlashIcon` field (after line 160):

```csharp
    // Filter/funnel glyph, same inline-SVG convention as the eye icons above.
    private static readonly MarkupString FunnelIcon = new(
        "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><polygon points=\"22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3\"/></svg>");
```

- [ ] **Step 2: Add the new fields and handler methods**

Add these fields right after `private readonly HashSet<long> _selectedThreadIds = new();` (line 176):

```csharp
    private readonly HashSet<long> _selectedThreadIds = new();
    private FilterPickerSheet? _filterSheet;
    private bool _isFilterAssignMode;
```

Add these methods right after `AllSelectedAreRead()` (after line 451, before `ToggleFavoriteSelectedThreads`):

```csharp
    private bool AllSelectedHaveFilter(long filterId) =>
        _selectedThreadIds.Count > 0 &&
        _selectedThreadIds.All(id => ViewModel.Threads.FirstOrDefault(t => t.Id == id)?.FilterIds.Contains(filterId) == true);

    private void OpenFilterBrowsePicker()
    {
        _isFilterAssignMode = false;
        _filterSheet?.Open();
    }

    private void OpenFilterAssignPicker()
    {
        _isFilterAssignMode = true;
        _filterSheet?.Open();
    }

    private void OnFilterSheetClosed()
    {
        // Opening the assign sheet keeps multi-select active (checkbox state depends on the
        // selection staying stable while the sheet is open); closing it — from any path,
        // outside-tap or hardware back — is when selection mode actually ends.
        if (_isFilterAssignMode)
        {
            ClearSelection();
            StateHasChanged();
        }
    }

    private bool IsFilterChecked(ForgeLinkSms.Core.Models.Filter filter) =>
        _isFilterAssignMode
            ? AllSelectedHaveFilter(filter.Id)
            : ViewModel.ActiveFilterIds.Contains(filter.Id);

    private async Task OnFilterToggled(ForgeLinkSms.Core.Models.Filter filter)
    {
        if (_isFilterAssignMode)
        {
            await ViewModel.ToggleFilterForThreadsCommand.ExecuteAsync((_selectedThreadIds.ToList(), filter.Id));
        }
        else
        {
            ViewModel.ToggleActiveFilter(filter.Id);
        }
        StateHasChanged();
    }

    private void OpenFilterMaintenance()
    {
        _filterSheet?.Close();
        Nav.NavigateTo("/filters");
    }
```

- [ ] **Step 3: Extend the back-handler to close the sheet first**

Rename `TryClearSelectionForBack` to `TryCloseOverlayForBack` and extend it. Replace (lines 199-212):

```csharp
    // The hardware back button is fully intercepted at the native layer (see MainActivity) to fix
    // an unrelated bug, so it no longer reaches this page's own click handlers — it must clear an
    // active multi-select itself instead of falling through to exiting the app.
    private bool TryClearSelectionForBack()
    {
        if (_selectedThreadIds.Count == 0)
        {
            return false;
        }

        ClearSelection();
        StateHasChanged();
        return true;
    }
```

with:

```csharp
    // The hardware back button is fully intercepted at the native layer (see MainActivity) to fix
    // an unrelated bug, so it no longer reaches this page's own click handlers or the filter
    // sheet's own click-outside-to-close handler — it must close the sheet or clear an active
    // multi-select itself instead of falling through to exiting the app.
    private bool TryCloseOverlayForBack()
    {
        if (_filterSheet is { IsOpen: true })
        {
            _filterSheet.Close();
            return true;
        }

        if (_selectedThreadIds.Count == 0)
        {
            return false;
        }

        ClearSelection();
        StateHasChanged();
        return true;
    }
```

Then update the two other references to the old name: in `OnInitializedAsync` (line 196), change `HistoryTracker.LocalBackHandler = TryClearSelectionForBack;` to `HistoryTracker.LocalBackHandler = TryCloseOverlayForBack;`. In `Dispose()` (line 240), change `if (HistoryTracker.LocalBackHandler == TryClearSelectionForBack)` to `if (HistoryTracker.LocalBackHandler == TryCloseOverlayForBack)`.

- [ ] **Step 4: Add the funnel button to the multi-select toolbar**

In the multi-select toolbar `<div>` (lines 16-27), insert a new button right after the favorite button (after line 19, before the existing read-state button):

```razor
            <button @onclick="ToggleFavoriteSelectedThreads" style="width:36px;height:32px;border:none;background:transparent;font-size:1em;line-height:1;padding:0;">@(AllSelectedAreFavorited() ? "💔" : "❤️")</button>
            <button @onclick="OpenFilterAssignPicker" style="width:36px;height:32px;border:none;background:transparent;display:flex;align-items:center;justify-content:center;padding:0;">@FunnelIcon</button>
            <button @onclick="ToggleSelectedThreadsReadState" style="width:36px;height:32px;border:none;border-radius:50%;background:#e2e8f0;display:flex;align-items:center;justify-content:center;padding:0;">@(AllSelectedAreRead() ? EyeSlashIcon : EyeOpenIcon)</button>
```

- [ ] **Step 5: Add the funnel button to the header, left-justified**

Replace the header's left-justified group (lines 32-40):

```razor
            <div style="display:flex;align-items:center;gap:10px;">
                <button @onclick="ToggleUnreadFilter"
                        style="width:32px;height:32px;border-radius:50%;border:none;padding:0;display:flex;align-items:center;justify-content:center;background:@(ViewModel.ShowUnreadOnly ? "var(--accent-color)" : "#e2e8f0");color:@(ViewModel.ShowUnreadOnly ? "white" : "#1e293b");">
                    @(ViewModel.ShowUnreadOnly ? EyeSlashIcon : EyeOpenIcon)
                </button>
                <button @onclick="OpenSearch"
                        style="width:32px;height:32px;border-radius:50%;border:none;background:#e2e8f0;font-size:0.9em;padding:0;">
                    🔍
                </button>
            </div>
```

with:

```razor
            <div style="display:flex;align-items:center;gap:10px;">
                <button @onclick="ToggleUnreadFilter"
                        style="width:32px;height:32px;border-radius:50%;border:none;padding:0;display:flex;align-items:center;justify-content:center;background:@(ViewModel.ShowUnreadOnly ? "var(--accent-color)" : "#e2e8f0");color:@(ViewModel.ShowUnreadOnly ? "white" : "#1e293b");">
                    @(ViewModel.ShowUnreadOnly ? EyeSlashIcon : EyeOpenIcon)
                </button>
                <button @onclick="OpenFilterBrowsePicker"
                        style="width:32px;height:32px;border-radius:50%;border:none;padding:0;display:flex;align-items:center;justify-content:center;background:@(ViewModel.ActiveFilterIds.Count > 0 ? "var(--accent-color)" : "#e2e8f0");color:@(ViewModel.ActiveFilterIds.Count > 0 ? "white" : "#1e293b");">
                    @FunnelIcon
                </button>
                <button @onclick="OpenSearch"
                        style="width:32px;height:32px;border-radius:50%;border:none;background:#e2e8f0;font-size:0.9em;padding:0;">
                    🔍
                </button>
            </div>
```

- [ ] **Step 6: Add colored filter dots to each thread row**

In the row markup (lines 103-114), replace:

```razor
                <div style="flex:1;min-width:0;">
                    <strong>@thread.DisplayNameOrAddress</strong>
                    @if (thread.IsFavorite)
                    {
                        <span> ❤️</span>
                    }
                    @if (thread.UnreadCount > 0)
                    {
                        <span style="color:#06b6d4;"> ● @thread.UnreadCount</span>
                    }
                    <div style="color:#94a3b8;font-size:0.85em;">@thread.PreviewText</div>
                </div>
```

with:

```razor
                <div style="flex:1;min-width:0;">
                    <strong>@thread.DisplayNameOrAddress</strong>
                    @if (thread.IsFavorite)
                    {
                        <span> ❤️</span>
                    }
                    @foreach (var filterId in thread.FilterIds)
                    {
                        var filter = ViewModel.Filters.FirstOrDefault(f => f.Id == filterId);
                        if (filter is not null)
                        {
                            <span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:@filter.ColorHex;margin-left:4px;"></span>
                        }
                    }
                    @if (thread.UnreadCount > 0)
                    {
                        <span style="color:#06b6d4;"> ● @thread.UnreadCount</span>
                    }
                    <div style="color:#94a3b8;font-size:0.85em;">@thread.PreviewText</div>
                </div>
```

- [ ] **Step 7: Add the `FilterPickerSheet` to the page**

Right after the action-sheet block (after line 152, before the closing `@code {`), add:

```razor
<FilterPickerSheet @ref="_filterSheet"
                    Filters="ViewModel.Filters"
                    IsChecked="IsFilterChecked"
                    OnToggle="OnFilterToggled"
                    ShowManageLink="!_isFilterAssignMode"
                    OnManageFilters="OpenFilterMaintenance"
                    OnClosed="OnFilterSheetClosed" />
```

- [ ] **Step 8: Build**

Run: `dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android -c Debug -v q -p:EmbedAssembliesIntoApk=true`
Expected: `Build succeeded.`

- [ ] **Step 9: Run the full test suite to confirm nothing regressed**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj`
Expected: PASS — this task doesn't touch `ForgeLinkSms.Core.Tests`, but it's a cheap sanity check before deploying.

- [ ] **Step 10: Deploy and manually verify on-device**

```bash
adb install -r src/ForgeLinkSms/bin/Debug/net9.0-android/com.companyname.forgelinksms-Signed.apk
```

Verify, using the running app (there are no filters yet at this point — Task 5 adds the way to create them, so this step confirms the *empty-state* wiring only):

- Open the header funnel picker: it shows "No filters yet." and a working "Manage filters" link that currently 404s or shows a blank page (expected — `/filters` doesn't exist until Task 5; just confirm navigation is attempted and doesn't crash the app).
- Long-press a thread to enter multi-select, confirm the new funnel button appears between ❤️ and the eye icon, and tapping it opens the same sheet (with "Manage filters" hidden this time).
- With the assign sheet open, press the hardware back button: confirm it closes the sheet and returns to the thread list with the row still selected being deselected (selection clears), rather than leaving the Conversations screen.

- [ ] **Step 11: Commit**

```bash
git add src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor
git commit -m "feat: wire filter browsing and assignment into ConversationsPage"
```

---

### Task 5: Filter Maintenance page

**Files:**
- Create: `src/ForgeLinkSms.Core/ViewModels/FiltersViewModel.cs`
- Create: `src/ForgeLinkSms/Pages/Filters/FiltersPage.razor`
- Modify: `src/ForgeLinkSms/Pages/Menu/MenuPage.razor`
- Modify: `src/ForgeLinkSms/MauiProgram.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/FiltersViewModelTests.cs`

**Interfaces:**
- Consumes: `IFilterRepository` from Task 1.
- Produces: a working `/filters` route reachable from the menu and from the picker sheet built in Tasks 3-4.

- [ ] **Step 1: Write the failing tests**

`tests/ForgeLinkSms.Core.Tests/ViewModels/FiltersViewModelTests.cs`:

```csharp
using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class FiltersViewModelTests
{
    [Fact]
    public async Task LoadCommand_populates_Filters_from_the_repository()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        var viewModel = new FiltersViewModel(repository.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Filters);
        Assert.Equal("Work", viewModel.Filters[0].Name);
    }

    [Fact]
    public async Task CreateFilterCommand_creates_with_the_current_name_and_color_and_resets_the_inputs()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>());
        repository.Setup(r => r.CreateFilterAsync("Work", "#ef4444"))
            .ReturnsAsync(new Filter { Id = 1, Name = "Work", ColorHex = "#ef4444" });
        var viewModel = new FiltersViewModel(repository.Object);
        viewModel.NewFilterName = "Work";
        viewModel.NewFilterColorHex = "#ef4444";

        await viewModel.CreateFilterCommand.ExecuteAsync(null);

        repository.Verify(r => r.CreateFilterAsync("Work", "#ef4444"), Times.Once);
        Assert.Single(viewModel.Filters);
        Assert.Equal(string.Empty, viewModel.NewFilterName);
        Assert.Equal(FiltersViewModel.PresetColors[0], viewModel.NewFilterColorHex);
    }

    [Fact]
    public async Task CreateFilterCommand_does_nothing_when_the_name_is_blank()
    {
        var repository = new Mock<IFilterRepository>();
        var viewModel = new FiltersViewModel(repository.Object);
        viewModel.NewFilterName = "   ";

        await viewModel.CreateFilterCommand.ExecuteAsync(null);

        repository.Verify(r => r.CreateFilterAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RenameFilterCommand_renames_in_the_repository_and_in_the_list()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RenameFilterCommand.ExecuteAsync((1L, "Personal"));

        repository.Verify(r => r.RenameFilterAsync(1, "Personal"), Times.Once);
        Assert.Equal("Personal", viewModel.Filters[0].Name);
    }

    [Fact]
    public async Task SetFilterColorCommand_updates_the_repository_and_the_list()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.SetFilterColorCommand.ExecuteAsync((1L, "#ef4444"));

        repository.Verify(r => r.SetFilterColorAsync(1, "#ef4444"), Times.Once);
        Assert.Equal("#ef4444", viewModel.Filters[0].ColorHex);
    }

    [Fact]
    public async Task DeleteFilterCommand_deletes_from_the_repository_and_removes_it_from_the_list()
    {
        var repository = new Mock<IFilterRepository>();
        repository.Setup(r => r.GetAllFiltersAsync()).ReturnsAsync(new List<Filter>
        {
            new() { Id = 1, Name = "Work", ColorHex = "#6366f1" }
        });
        var viewModel = new FiltersViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteFilterCommand.ExecuteAsync(1L);

        repository.Verify(r => r.DeleteFilterAsync(1), Times.Once);
        Assert.Empty(viewModel.Filters);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj --filter FiltersViewModelTests`
Expected: FAIL to build — `FiltersViewModel` doesn't exist yet.

- [ ] **Step 3: Write `FiltersViewModel`**

`src/ForgeLinkSms.Core/ViewModels/FiltersViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.ViewModels;

public partial class FiltersViewModel : ObservableObject
{
    private readonly IFilterRepository _filterRepository;

    public static readonly IReadOnlyList<string> PresetColors = new[]
    {
        "#ef4444", "#f97316", "#eab308", "#22c55e", "#06b6d4", "#6366f1", "#a855f7", "#ec4899"
    };

    public ObservableCollection<Filter> Filters { get; } = new();

    [ObservableProperty]
    private string _newFilterName = string.Empty;

    [ObservableProperty]
    private string _newFilterColorHex = PresetColors[0];

    public FiltersViewModel(IFilterRepository filterRepository)
    {
        _filterRepository = filterRepository;
    }

    [RelayCommand]
    private async Task Load()
    {
        Filters.Clear();
        foreach (var filter in await _filterRepository.GetAllFiltersAsync())
        {
            Filters.Add(filter);
        }
    }

    [RelayCommand]
    private async Task CreateFilter()
    {
        var name = NewFilterName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var filter = await _filterRepository.CreateFilterAsync(name, NewFilterColorHex);
        Filters.Add(filter);
        NewFilterName = string.Empty;
        NewFilterColorHex = PresetColors[0];
    }

    [RelayCommand]
    private async Task RenameFilter((long FilterId, string NewName) args)
    {
        var name = args.NewName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        await _filterRepository.RenameFilterAsync(args.FilterId, name);
        var filter = Filters.FirstOrDefault(f => f.Id == args.FilterId);
        if (filter is not null)
        {
            filter.Name = name;
        }
    }

    [RelayCommand]
    private async Task SetFilterColor((long FilterId, string ColorHex) args)
    {
        await _filterRepository.SetFilterColorAsync(args.FilterId, args.ColorHex);
        var filter = Filters.FirstOrDefault(f => f.Id == args.FilterId);
        if (filter is not null)
        {
            filter.ColorHex = args.ColorHex;
        }
    }

    [RelayCommand]
    private async Task DeleteFilter(long filterId)
    {
        await _filterRepository.DeleteFilterAsync(filterId);
        var filter = Filters.FirstOrDefault(f => f.Id == filterId);
        if (filter is not null)
        {
            Filters.Remove(filter);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj --filter FiltersViewModelTests`
Expected: PASS, all 6 tests green.

- [ ] **Step 5: Write `FiltersPage.razor`**

`src/ForgeLinkSms/Pages/Filters/FiltersPage.razor`:

```razor
@page "/filters"
@inject ForgeLinkSms.Core.ViewModels.FiltersViewModel ViewModel

<div style="padding:12px;">
    <h3>Filters</h3>

    <div style="display:flex;flex-direction:column;gap:8px;padding:12px 0;border-bottom:1px solid #eee;margin-bottom:12px;">
        <input placeholder="New filter name" @bind="ViewModel.NewFilterName" @bind:event="oninput"
               style="padding:8px;border-radius:8px;border:1px solid #cbd5e1;font-size:1em;" />
        <div style="display:flex;gap:8px;flex-wrap:wrap;">
            @foreach (var color in ForgeLinkSms.Core.ViewModels.FiltersViewModel.PresetColors)
            {
                <div @onclick="() => ViewModel.NewFilterColorHex = color"
                     style="width:28px;height:28px;border-radius:50%;background:@color;cursor:pointer;border:3px solid @(ViewModel.NewFilterColorHex == color ? "#1e293b" : "transparent");"></div>
            }
        </div>
        <button @onclick="() => ViewModel.CreateFilterCommand.ExecuteAsync(null)"
                disabled="@(string.IsNullOrWhiteSpace(ViewModel.NewFilterName))"
                style="padding:8px;border-radius:8px;border:none;background:var(--accent-color);color:white;font-size:1em;">
            Add
        </button>
    </div>

    @if (ViewModel.Filters.Count == 0)
    {
        <p>No filters yet.</p>
    }
    else
    {
        @foreach (var filter in ViewModel.Filters)
        {
            <div style="padding:10px 0;border-bottom:1px solid #eee;">
                <div style="display:flex;align-items:center;gap:10px;">
                    <div @onclick="() => ToggleColorPicker(filter.Id)"
                         style="width:24px;height:24px;border-radius:50%;background:@filter.ColorHex;cursor:pointer;flex-shrink:0;"></div>

                    @if (_editingFilterId == filter.Id)
                    {
                        <input @bind="_editingName" @bind:event="oninput" @onkeyup="@(e => OnRenameKeyUp(e, filter.Id))"
                               style="flex:1;padding:6px;border-radius:6px;border:1px solid #cbd5e1;" />
                        <button @onclick="() => ConfirmRename(filter.Id)" style="border:none;background:transparent;">✓</button>
                    }
                    else
                    {
                        <span @onclick="() => StartRename(filter)" style="flex:1;cursor:pointer;">@filter.Name</span>
                    }

                    <button @onclick="() => ViewModel.DeleteFilterCommand.ExecuteAsync(filter.Id)"
                            style="border:none;background:transparent;font-size:1.1em;">🗑</button>
                </div>

                @if (_colorPickerFilterId == filter.Id)
                {
                    <div style="display:flex;gap:8px;flex-wrap:wrap;padding:8px 0 0 34px;">
                        @foreach (var color in ForgeLinkSms.Core.ViewModels.FiltersViewModel.PresetColors)
                        {
                            <div @onclick="() => SetColor(filter.Id, color)"
                                 style="width:24px;height:24px;border-radius:50%;background:@color;cursor:pointer;border:3px solid @(filter.ColorHex == color ? "#1e293b" : "transparent");"></div>
                        }
                    </div>
                }
            </div>
        }
    }
</div>

@code {
    private long? _editingFilterId;
    private string _editingName = string.Empty;
    private long? _colorPickerFilterId;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void StartRename(ForgeLinkSms.Core.Models.Filter filter)
    {
        _editingFilterId = filter.Id;
        _editingName = filter.Name;
        _colorPickerFilterId = null;
    }

    private async Task ConfirmRename(long filterId)
    {
        await ViewModel.RenameFilterCommand.ExecuteAsync((filterId, _editingName));
        _editingFilterId = null;
    }

    private async Task OnRenameKeyUp(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e, long filterId)
    {
        if (e.Key == "Enter")
        {
            await ConfirmRename(filterId);
        }
    }

    private void ToggleColorPicker(long filterId)
    {
        _colorPickerFilterId = _colorPickerFilterId == filterId ? null : filterId;
        _editingFilterId = null;
    }

    private async Task SetColor(long filterId, string color)
    {
        await ViewModel.SetFilterColorCommand.ExecuteAsync((filterId, color));
        _colorPickerFilterId = null;
    }
}
```

- [ ] **Step 6: Add the "Filters" row to the menu**

In `src/ForgeLinkSms/Pages/Menu/MenuPage.razor`, insert this right after the Blocked row (after line 40, before the Scheduled row):

```razor
    <div @onclick="@(() => Nav.NavigateTo("/blocked"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🚫 Blocked
    </div>
    <div @onclick="@(() => Nav.NavigateTo("/filters"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        🏷️ Filters
    </div>
```

- [ ] **Step 7: Register `FiltersViewModel` in `MauiProgram.cs`**

In `src/ForgeLinkSms/MauiProgram.cs`, right after the `filterRepository` registration added in Task 1 (`builder.Services.AddSingleton<IFilterRepository>(filterRepository);`), add:

```csharp
		builder.Services.AddTransient<FiltersViewModel>();
```

- [ ] **Step 8: Build**

Run: `dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android -c Debug -v q -p:EmbedAssembliesIntoApk=true`
Expected: `Build succeeded.`

- [ ] **Step 9: Deploy and manually verify the full feature end-to-end on-device**

```bash
adb install -r src/ForgeLinkSms/bin/Debug/net9.0-android/com.companyname.forgelinksms-Signed.apk
```

Walk through the entire feature:

1. Open the menu (profile icon on Conversations) → tap "🏷️ Filters". Confirm the page loads with "No filters yet."
2. Type a name, pick a color swatch, tap Add. Confirm the new filter appears in the list below with the right color and name.
3. Add a second filter with a different name/color.
4. Tap a filter's color swatch, pick a different preset — confirm it updates immediately.
5. Tap a filter's name, edit it, hit Enter (or the ✓ button) — confirm the rename sticks.
6. Go back to Conversations. Long-press two different threads to multi-select them, tap the new funnel toolbar button. Confirm the sheet shows both filters unchecked, tap one — confirm both threads now show that filter's colored dot in the list, and the sheet stayed open (selection didn't clear).
7. Tap the same filter again in the still-open sheet — confirm it unassigns from both (dots disappear) since both had it.
8. Re-assign one filter to one thread only, close the sheet (tap outside it) — confirm selection mode exits.
9. Tap the header funnel icon (left of search) — confirm it lists both filters, tap one — confirm the list narrows to only threads with that filter, and the funnel icon turns accent-colored.
10. Tap a second filter in that same header sheet (without closing) — confirm the list now shows threads matching *either* filter (OR semantics).
11. Toggle the unread-eye filter on top of that — confirm it narrows further (AND with the active filters).
12. Tap the funnel icon again and toggle both filters off — confirm the full list returns and the funnel icon returns to its neutral color.
13. Press hardware back while the header filter sheet is open — confirm it closes the sheet without leaving Conversations.
14. Go back to Filter Maintenance and delete one of the filters — confirm it disappears from the maintenance list, and back on Conversations its colored dots are gone from every thread that had it and it's gone from the header/assign pickers.

- [ ] **Step 10: Commit**

```bash
git add src/ForgeLinkSms.Core/ViewModels/FiltersViewModel.cs src/ForgeLinkSms/Pages/Filters/FiltersPage.razor src/ForgeLinkSms/Pages/Menu/MenuPage.razor src/ForgeLinkSms/MauiProgram.cs tests/ForgeLinkSms.Core.Tests/ViewModels/FiltersViewModelTests.cs
git commit -m "feat: add Filter Maintenance page and wire it into the menu"
```
