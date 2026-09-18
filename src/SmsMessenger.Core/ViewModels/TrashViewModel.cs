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
