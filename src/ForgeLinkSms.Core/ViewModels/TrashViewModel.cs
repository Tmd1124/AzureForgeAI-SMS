using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class TrashViewModel : ObservableObject
{
    private const int ExpiryDays = 30;

    private readonly ITrashRepository _trashRepository;
    private readonly IThreadService _threadService;
    private readonly IThreadDeletionService _threadDeletionService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IFilterRepository _filterRepository;

    public ObservableCollection<SmsThread> TrashedThreads { get; } = new();

    public TrashViewModel(
        ITrashRepository trashRepository,
        IThreadService threadService,
        IThreadDeletionService threadDeletionService,
        IFavoriteRepository favoriteRepository,
        IFilterRepository filterRepository)
    {
        _trashRepository = trashRepository;
        _threadService = threadService;
        _threadDeletionService = threadDeletionService;
        _favoriteRepository = favoriteRepository;
        _filterRepository = filterRepository;
    }

    [RelayCommand]
    private async Task Load()
    {
        // Anything trashed 30+ days ago is permanently gone before the list is even shown, so
        // the page never displays a thread that's about to vanish mid-visit.
        var expiredIds = await _trashRepository.GetExpiredThreadIdsAsync(DateTimeOffset.UtcNow.AddDays(-ExpiryDays));
        foreach (var expiredId in expiredIds)
        {
            await PermanentlyDeleteAsync(expiredId);
        }

        TrashedThreads.Clear();
        // Excludes expiredIds client-side rather than re-querying: PermanentlyDeleteAsync
        // already removed their TrashedThread rows above, so a second fetch would just be an
        // extra round-trip to confirm what this method already knows.
        var trashedIds = (await _trashRepository.GetTrashedThreadIdsAsync()).Except(expiredIds).ToList();
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

    [RelayCommand]
    private async Task DeleteThreads(IReadOnlyList<long> threadIds)
    {
        foreach (var threadId in threadIds)
        {
            await PermanentlyDeleteAsync(threadId);
        }

        await Load();
    }

    // Erases the real messages, then cleans up every app-local record that references this
    // thread ID — including the TrashedThread row itself, via RestoreThreadAsync's existing
    // delete-the-tracking-row behavior (the same effect "restoring" needs, just because the
    // thread is gone rather than un-trashed).
    private async Task PermanentlyDeleteAsync(long threadId)
    {
        await _threadDeletionService.DeleteThreadAsync(threadId);
        await _favoriteRepository.UnfavoriteThreadAsync(threadId);
        await _filterRepository.RemoveAllAssignmentsForThreadAsync(threadId);
        await _trashRepository.RestoreThreadAsync(threadId);
    }
}
