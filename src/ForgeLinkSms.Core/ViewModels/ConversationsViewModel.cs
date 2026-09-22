using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ConversationsViewModel : ObservableObject
{
    private readonly IThreadService _threadService;
    private readonly ITrashRepository _trashRepository;
    private readonly IContactBlockService _blockService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IUndoStack _undoStack;
    private readonly IMarkAsReadService _markAsReadService;
    private readonly IArchiveRepository _archiveRepository;
    private readonly IFilterRepository _filterRepository;
    private IReadOnlyList<SmsThread> _allThreads = Array.Empty<SmsThread>();
    private bool _isLoadingThreads;

    public ObservableCollection<SmsThread> Threads { get; } = new();
    public ObservableCollection<Filter> Filters { get; } = new();
    public HashSet<long> ActiveFilterIds { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Threads))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Threads))]
    private bool _showUnreadOnly;

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

    [RelayCommand]
    private async Task MarkThreadReadState(long threadId)
    {
        var thread = _allThreads.FirstOrDefault(t => t.Id == threadId);
        if (thread is null)
        {
            return;
        }

        if (thread.UnreadCount > 0)
        {
            await _markAsReadService.MarkThreadAsReadAsync(threadId);
            _undoStack.Push(new MarkAsReadUndoAction(new[] { threadId }, _markAsReadService));
            thread.UnreadCount = 0;
        }
        else
        {
            await _markAsReadService.MarkThreadsAsUnreadAsync(new[] { threadId });
            thread.UnreadCount = 1;
        }

        SortThreads();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task FavoriteThread(long threadId)
    {
        var thread = _allThreads.FirstOrDefault(t => t.Id == threadId);
        if (thread is null)
        {
            return;
        }

        if (thread.IsFavorite)
        {
            await _favoriteRepository.UnfavoriteThreadAsync(threadId);
            thread.IsFavorite = false;
        }
        else
        {
            await _favoriteRepository.FavoriteThreadAsync(threadId);
            thread.IsFavorite = true;
        }

        SortThreads();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task FavoriteThreads(IReadOnlyList<long> threadIds)
    {
        var threads = threadIds
            .Select(id => _allThreads.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Cast<SmsThread>()
            .ToList();
        if (threads.Count == 0)
        {
            return;
        }

        // Mirrors the single-item toggle: if every selected thread is already a favorite,
        // the action removes them all; otherwise it favorites everything selected. No undo
        // here, matching the existing single-item Favorite toggle.
        var allFavorited = threads.All(t => t.IsFavorite);
        foreach (var thread in threads)
        {
            if (allFavorited)
            {
                await _favoriteRepository.UnfavoriteThreadAsync(thread.Id);
                thread.IsFavorite = false;
            }
            else
            {
                await _favoriteRepository.FavoriteThreadAsync(thread.Id);
                thread.IsFavorite = true;
            }
        }

        SortThreads();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task TrashThread(long threadId)
    {
        await _trashRepository.TrashThreadAsync(threadId);
        _undoStack.Push(new TrashUndoAction(threadId, _trashRepository));
        RemoveThreadsFromList(new[] { threadId });
    }

    [RelayCommand]
    private async Task ArchiveThread(long threadId)
    {
        await _archiveRepository.ArchiveThreadAsync(threadId);
        _undoStack.Push(new ArchiveUndoAction(threadId, _archiveRepository));
        RemoveThreadsFromList(new[] { threadId });
    }

    [RelayCommand]
    private async Task TrashThreads(IReadOnlyList<long> threadIds)
    {
        var undoActions = new List<IUndoableAction>();
        foreach (var threadId in threadIds)
        {
            await _trashRepository.TrashThreadAsync(threadId);
            undoActions.Add(new TrashUndoAction(threadId, _trashRepository));
        }
        _undoStack.Push(new BulkUndoAction(undoActions, $"Trashed {threadIds.Count} conversation(s)"));
        RemoveThreadsFromList(threadIds);
    }

    [RelayCommand]
    private async Task ArchiveThreads(IReadOnlyList<long> threadIds)
    {
        var undoActions = new List<IUndoableAction>();
        foreach (var threadId in threadIds)
        {
            await _archiveRepository.ArchiveThreadAsync(threadId);
            undoActions.Add(new ArchiveUndoAction(threadId, _archiveRepository));
        }
        _undoStack.Push(new BulkUndoAction(undoActions, $"Archived {threadIds.Count} conversation(s)"));
        RemoveThreadsFromList(threadIds);
    }

    [RelayCommand]
    private async Task Undo()
    {
        // Undo restores a thread from trash/archive or flips a read flag that this ViewModel
        // doesn't otherwise track in memory (e.g. re-inserting a favorited-then-trashed thread),
        // so a full reload is worth the cost here — undo is rare, unlike the actions above.
        await _undoStack.UndoAsync();
        await Load();
    }

    [RelayCommand]
    private async Task MarkThreadsReadState(IReadOnlyList<long> threadIds)
    {
        var threads = threadIds
            .Select(id => _allThreads.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Cast<SmsThread>()
            .ToList();
        if (threads.Count == 0)
        {
            return;
        }

        // Only a selection that's entirely unread resolves to "mark read" — an all-read
        // selection or a mixed one both resolve to "mark unread", since that's the one outcome
        // that isn't ambiguous no matter what was selected.
        var allUnread = threads.All(t => t.UnreadCount > 0);
        if (allUnread)
        {
            await _markAsReadService.MarkThreadsAsReadAsync(threadIds);
            _undoStack.Push(new MarkAsReadUndoAction(threadIds, _markAsReadService));
            foreach (var thread in threads)
            {
                thread.UnreadCount = 0;
            }
        }
        else
        {
            await _markAsReadService.MarkThreadsAsUnreadAsync(threadIds);
            foreach (var thread in threads)
            {
                thread.UnreadCount = 1;
            }
        }

        SortThreads();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task BlockThread(string address)
    {
        var normalizedAddress = PhoneNumberFormatter.ToComparableDigits(address);
        await _blockService.BlockAsync(normalizedAddress);
        _undoStack.Push(new BlockUndoAction(normalizedAddress, _blockService));
        _allThreads = _allThreads.Where(t => PhoneNumberFormatter.ToComparableDigits(t.Address) != normalizedAddress).ToList();
        ApplyFilter();
    }

    private void SortThreads()
    {
        _allThreads = _allThreads
            .OrderByDescending(t => t.IsFavorite)
            .ThenByDescending(t => t.UnreadCount > 0)
            .ThenByDescending(t => t.LastMessageTimestamp)
            .ToList();
    }

    private void RemoveThreadsFromList(IReadOnlyCollection<long> threadIds)
    {
        _allThreads = _allThreads.Where(t => !threadIds.Contains(t.Id)).ToList();
        ApplyFilter();
    }

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

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowUnreadOnlyChanged(bool value) => ApplyFilter();

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
}
