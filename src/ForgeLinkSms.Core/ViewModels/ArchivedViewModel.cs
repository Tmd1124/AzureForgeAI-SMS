using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ArchivedViewModel : ObservableObject
{
    private readonly IArchiveRepository _archiveRepository;
    private readonly IThreadService _threadService;

    public ObservableCollection<SmsThread> ArchivedThreads { get; } = new();

    public ArchivedViewModel(IArchiveRepository archiveRepository, IThreadService threadService)
    {
        _archiveRepository = archiveRepository;
        _threadService = threadService;
    }

    [RelayCommand]
    private async Task Load()
    {
        ArchivedThreads.Clear();
        var archivedIds = await _archiveRepository.GetArchivedThreadIdsAsync();
        if (archivedIds.Count == 0)
        {
            return;
        }

        var allThreads = await _threadService.GetThreadsAsync();
        foreach (var thread in allThreads.Where(t => archivedIds.Contains(t.Id)))
        {
            ArchivedThreads.Add(thread);
        }
    }

    [RelayCommand]
    private async Task Unarchive(long threadId)
    {
        await _archiveRepository.UnarchiveThreadAsync(threadId);
        await Load();
    }
}
