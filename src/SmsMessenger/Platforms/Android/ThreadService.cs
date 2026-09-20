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
        var rawRows = new List<(long ThreadId, string Address, string Body, long DateMillis, int Unread)>();

        // Telephony.Threads only carries thread ids; the snippet (address,
        // body, date, read) is read straight from Telephony.Sms grouped by
        // thread_id, which is the simplest way to build the list view.
        var projection = new[] { "thread_id", "address", "body", "date", "read" };
        using var cursor = context.ContentResolver!.Query(
            AndroidTelephony.Sms.ContentUri!, projection, null, null, "date DESC");

        if (cursor is null)
        {
            return Array.Empty<SmsThread>();
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

            rawRows.Add((
                threadId,
                cursor.GetString(addressIdx) ?? string.Empty,
                cursor.GetString(bodyIdx) ?? string.Empty,
                cursor.GetLong(dateIdx),
                cursor.GetInt(readIdx) == 0 ? 1 : 0));
        }

        // Each lookup is dispatched via Task.Run so the contact-provider query (a blocking
        // call) for every thread runs on its own thread-pool thread instead of one at a time —
        // with dozens of conversations, sequential awaits here were the dominant cost of loading
        // the list.
        var contacts = await Task.WhenAll(rawRows.Select(row => Task.Run(() => _contactService.LookupAsync(row.Address))));

        var results = new List<SmsThread>(rawRows.Count);
        for (var i = 0; i < rawRows.Count; i++)
        {
            var row = rawRows[i];
            var contact = contacts[i];
            results.Add(new SmsThread
            {
                Id = row.ThreadId,
                Address = row.Address,
                DisplayName = contact?.DisplayName,
                PhotoUri = contact?.PhotoUri,
                LastMessageBody = row.Body,
                LastMessageTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(row.DateMillis),
                UnreadCount = row.Unread
            });
        }

        return results;
    }
}
