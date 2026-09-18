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
