using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace ForgeLinkSms.Platforms.Android;

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

    public Task MarkThreadAsReadAsync(long threadId)
    {
        SetReadFlag(new[] { threadId }, read: 1);
        return Task.CompletedTask;
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
        var mmsUri = global::Android.Net.Uri.Parse("content://mms")!;

        foreach (var threadId in threadIds)
        {
            var args = new[] { threadId.ToString() };
            context.ContentResolver!.Update(AndroidTelephony.Sms.ContentUri!, values, "thread_id = ?", args);
            // A thread's unread state can come from either table (see ThreadService), so both
            // have to be flipped or a thread whose newest message is an MMS would stay stuck
            // showing unread after being opened/marked read.
            context.ContentResolver!.Update(mmsUri, values, "thread_id = ?", args);
        }
    }
}
