using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidTelephony = global::Android.Provider.Telephony;

namespace SmsMessenger.Platforms.Android;

public class ThreadService : IThreadService
{
    private readonly IContactService _contactService;

    public ThreadService(IContactService contactService)
    {
        _contactService = contactService;
    }

    public async Task<IReadOnlyList<SmsThread>> GetThreadsAsync()
    {
        var context = AndroidApp.Context;
        var results = new List<SmsThread>();

        // Telephony.Threads only carries thread ids; the snippet (address,
        // body, date, read) is read straight from Telephony.Sms grouped by
        // thread_id, which is the simplest way to build the list view.
        var projection = new[] { "thread_id", "address", "body", "date", "read" };
        using var cursor = context.ContentResolver!.Query(
            AndroidTelephony.Sms.ContentUri!, projection, null, null, "date DESC");

        if (cursor is null)
        {
            return results;
        }

        var seenThreadIds = new HashSet<long>();
        var threadIdIdx = cursor.GetColumnIndexOrThrow("thread_id");
        var addressIdx = cursor.GetColumnIndexOrThrow("address");
        var bodyIdx = cursor.GetColumnIndexOrThrow("body");
        var dateIdx = cursor.GetColumnIndexOrThrow("date");
        var readIdx = cursor.GetColumnIndexOrThrow("read");

        while (cursor.MoveToNext())
        {
            var threadId = cursor.GetLong(threadIdIdx);
            if (!seenThreadIds.Add(threadId))
            {
                continue; // already took the most recent row for this thread (query is DATE DESC)
            }

            var address = cursor.GetString(addressIdx) ?? string.Empty;
            var contact = await _contactService.LookupAsync(address);
            var unread = cursor.GetInt(readIdx) == 0 ? 1 : 0;

            results.Add(new SmsThread
            {
                Id = threadId,
                Address = address,
                DisplayName = contact?.DisplayName,
                PhotoUri = contact?.PhotoUri,
                LastMessageBody = cursor.GetString(bodyIdx) ?? string.Empty,
                LastMessageTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(dateIdx)),
                UnreadCount = unread
            });
        }

        return results;
    }
}
