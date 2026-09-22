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
    private readonly IThreadDeletionService _threadDeletionService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IFilterRepository _filterRepository;

    public ObservableCollection<SmsThread> ArchivedThreads { get; } = new();

    public ArchivedViewModel(
        IArchiveRepository archiveRepository,
        IThreadService threadService,
        IThreadDeletionService threadDeletionService,
        IFavoriteRepository favoriteRepository,
        IFilterRepository filterRepository)
    {
        _archiveRepository = archiveRepository;
        _threadService = threadService;
        _threadDeletionService = threadDeletionService;
        _favoriteRepository = favoriteRepository;
        _filterRepository = filterRepository;
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

    [RelayCommand]
    private async Task DeleteThreads(IReadOnlyList<long> threadIds)
    {
        foreach (var threadId in threadIds)
        {
            // Erases the real messages, then cleans up every app-local record that references
            // this thread ID — including the ArchivedThread row itself, via
            // UnarchiveThreadAsync's existing delete-the-tracking-row behavior.
            await _threadDeletionService.DeleteThreadAsync(threadId);
            await _favoriteRepository.UnfavoriteThreadAsync(threadId);
            await _filterRepository.RemoveAllAssignmentsForThreadAsync(threadId);
            await _archiveRepository.UnarchiveThreadAsync(threadId);
        }

        await Load();
    }
}
