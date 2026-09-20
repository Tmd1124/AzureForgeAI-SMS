using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.Utils;

namespace SmsMessenger.Core.ViewModels;

public partial class ConversationsViewModel : ObservableObject
{
    private readonly IThreadService _threadService;
    private readonly ITrashRepository _trashRepository;
    private readonly IContactBlockService _blockService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IUndoStack _undoStack;
    private readonly IMarkAsReadService _markAsReadService;
    private readonly IArchiveRepository _archiveRepository;
    private IReadOnlyList<SmsThread> _allThreads = Array.Empty<SmsThread>();
    private bool _isLoadingThreads;

    public ObservableCollection<SmsThread> Threads { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Threads))]
    private string _searchText = string.Empty;

    public ConversationsViewModel(
        IThreadService threadService,
        ITrashRepository trashRepository,
        IContactBlockService blockService,
        IFavoriteRepository favoriteRepository,
        IUndoStack undoStack,
        IMarkAsReadService markAsReadService,
        IArchiveRepository archiveRepository)
    {
        _threadService = threadService;
        _trashRepository = trashRepository;
        _blockService = blockService;
        _favoriteRepository = favoriteRepository;
        _undoStack = undoStack;
        _markAsReadService = markAsReadService;
        _archiveRepository = archiveRepository;
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

            _allThreads = threads
                .Where(t => !trashedIds.Contains(t.Id) && !blockedNumbers.Contains(t.Address) && !archivedIds.Contains(t.Id))
                .Select(t =>
                {
                    t.IsFavorite = favoriteIds.Contains(t.Id);
                    return t;
                })
                .OrderByDescending(t => t.IsFavorite)
                .ThenByDescending(t => t.UnreadCount > 0)
                .ThenByDescending(t => t.LastMessageTimestamp)
                .ToList();
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
        }
        else
        {
            await _markAsReadService.MarkThreadsAsUnreadAsync(new[] { threadId });
        }

        await Load();
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
        }
        else
        {
            await _favoriteRepository.FavoriteThreadAsync(threadId);
        }

        await Load();
    }

    [RelayCommand]
    private async Task TrashThread(long threadId)
    {
        await _trashRepository.TrashThreadAsync(threadId);
        _undoStack.Push(new TrashUndoAction(threadId, _trashRepository));
        await Load();
    }

    [RelayCommand]
    private async Task ArchiveThread(long threadId)
    {
        await _archiveRepository.ArchiveThreadAsync(threadId);
        _undoStack.Push(new ArchiveUndoAction(threadId, _archiveRepository));
        await Load();
    }

    [RelayCommand]
    private async Task BlockThread(string address)
    {
        var normalizedAddress = PhoneNumberFormatter.ToComparableDigits(address);
        await _blockService.BlockAsync(normalizedAddress);
        _undoStack.Push(new BlockUndoAction(normalizedAddress, _blockService));
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
