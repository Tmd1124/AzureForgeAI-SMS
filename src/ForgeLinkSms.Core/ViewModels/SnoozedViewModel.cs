using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class SnoozedViewModel : ObservableObject
{
    private readonly ISnoozeService _snoozeService;
    private readonly IThreadService _threadService;

    public ObservableCollection<(SmsThread Thread, DateTimeOffset UntilUtc)> SnoozedThreads { get; } = new();

    public SnoozedViewModel(ISnoozeService snoozeService, IThreadService threadService)
    {
        _snoozeService = snoozeService;
        _threadService = threadService;
    }

    [RelayCommand]
    private async Task Load()
    {
        SnoozedThreads.Clear();
        await _snoozeService.WakeExpiredAsync(DateTimeOffset.UtcNow);
        var snoozed = await _snoozeService.GetSnoozedAsync();
        if (snoozed.Count == 0)
        {
            return;
        }

        var threads = await _threadService.GetThreadsAsync();
        foreach (var thread in threads.Where(t => snoozed.ContainsKey(t.Id)).OrderBy(t => snoozed[t.Id]))
        {
            SnoozedThreads.Add((thread, snoozed[thread.Id]));
        }
    }

    [RelayCommand]
    private async Task Unsnooze(long threadId)
    {
        await _snoozeService.UnsnoozeAsync(threadId);
        await Load();
    }
}
