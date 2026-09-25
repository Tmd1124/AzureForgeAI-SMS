using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidTelephony = global::Android.Provider.Telephony;

namespace ForgeLinkSms.Platforms.Android;

public class ThreadService : IThreadService
{
    private readonly IContactService _contactService;

    public ThreadService(IContactService contactService)
    {
        _contactService = contactService;
    }

    private sealed class ThreadLatestMessage
    {
        public required long ThreadId { get; init; }
        public required long DateMillis { get; init; }
        public required string Address { get; init; }
        public required string Body { get; init; }
        public required bool IsUnread { get; init; }
    }

    // Android's SMS "type" column: 1 = inbox; every other value (sent, outbox, failed, queued)
    // is a message this user wrote.
    private const int SmsTypeInbox = 1;

    public async Task<IReadOnlyList<SmsThread>> GetThreadsAsync()
    {
        var context = AndroidApp.Context;

        // The whole scan (including reading and base64-decoding any MMS image/GIF that's the
        // newest message in a thread) is blocking ContentResolver work. Blazor Hybrid page
        // lifecycle callbacks run on the UI thread by default, so without Task.Run this can
        // freeze touch input long enough to trip Android's ANR watchdog on a device with many
        // MMS-heavy conversations.
        var rawRows = await Task.Run(() =>
        {
            // A thread's most recent activity can be either an SMS or an MMS (a photo, a group
            // text, many RCS fallbacks), and the two live in entirely separate content:// tables
            // with no shared "conversations" view this app can rely on — so both are read here
            // and merged per thread_id, keeping whichever side is newer.
            var latestByThread = new Dictionary<long, ThreadLatestMessage>();
            var threadsWithOutgoing = new HashSet<long>();
            ReadLatestSmsPerThread(context, latestByThread, threadsWithOutgoing);
            ReadLatestMmsPerThread(context, latestByThread, threadsWithOutgoing);
            return (Rows: latestByThread.Values.ToList(), ThreadsWithOutgoing: threadsWithOutgoing);
        });

        // Each lookup is dispatched via Task.Run so the contact-provider query (a blocking
        // call) for every thread runs on its own thread-pool thread instead of one at a time —
        // with dozens of conversations, sequential awaits here were the dominant cost of loading
        // the list.
        var contacts = await Task.WhenAll(rawRows.Rows.Select(row => Task.Run(() => _contactService.LookupAsync(row.Address))));

        var results = new List<SmsThread>(rawRows.Rows.Count);
        for (var i = 0; i < rawRows.Rows.Count; i++)
        {
            var row = rawRows.Rows[i];
            var contact = contacts[i];
            results.Add(new SmsThread
            {
                Id = row.ThreadId,
                Address = row.Address,
                DisplayName = contact?.DisplayName,
                PhotoUri = contact?.PhotoUri,
                LastMessageBody = row.Body,
                LastMessageTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(row.DateMillis),
                UnreadCount = row.IsUnread ? 1 : 0,
                HasOutgoing = rawRows.ThreadsWithOutgoing.Contains(row.ThreadId)
            });
        }

        return results;
    }

    private static void ReadLatestSmsPerThread(global::Android.Content.Context context, Dictionary<long, ThreadLatestMessage> latestByThread, HashSet<long> threadsWithOutgoing)
    {
        var projection = new[] { "thread_id", "address", "body", "date", "read", "type" };
        using var cursor = context.ContentResolver!.Query(AndroidTelephony.Sms.ContentUri!, projection, null, null, "date DESC");
        if (cursor is null)
        {
            return;
        }

        var threadIdIdx = cursor.GetColumnIndexOrThrow("thread_id");
        var addressIdx = cursor.GetColumnIndexOrThrow("address");
        var bodyIdx = cursor.GetColumnIndexOrThrow("body");
        var dateIdx = cursor.GetColumnIndexOrThrow("date");
        var readIdx = cursor.GetColumnIndexOrThrow("read");
        var typeIdx = cursor.GetColumnIndexOrThrow("type");

        while (cursor.MoveToNext())
        {
            var threadId = cursor.GetLong(threadIdIdx);
            if (cursor.GetInt(typeIdx) != SmsTypeInbox)
            {
                threadsWithOutgoing.Add(threadId);
            }
            var dateMillis = cursor.GetLong(dateIdx);
            if (latestByThread.TryGetValue(threadId, out var existing) && existing.DateMillis >= dateMillis)
            {
                continue; // already have a newer row (SMS or MMS) for this thread
            }

            latestByThread[threadId] = new ThreadLatestMessage
            {
                ThreadId = threadId,
                DateMillis = dateMillis,
                Address = cursor.GetString(addressIdx) ?? string.Empty,
                Body = cursor.GetString(bodyIdx) ?? string.Empty,
                IsUnread = cursor.GetInt(readIdx) == 0
            };
        }
    }

    private static void ReadLatestMmsPerThread(global::Android.Content.Context context, Dictionary<long, ThreadLatestMessage> latestByThread, HashSet<long> threadsWithOutgoing)
    {
        var allMms = MmsReader.QueryAll(context);
        threadsWithOutgoing.UnionWith(allMms.Where(m => m.IsOutgoing).Select(m => m.ThreadId));

        var latestMmsPerThread = allMms
            .GroupBy(m => m.ThreadId)
            .Select(g => g.OrderByDescending(m => m.Date).First());

        foreach (var mms in latestMmsPerThread)
        {
            var dateMillis = mms.Date.ToUnixTimeMilliseconds();
            if (latestByThread.TryGetValue(mms.ThreadId, out var existing) && existing.DateMillis >= dateMillis)
            {
                continue; // the thread's newest SMS is still newer than its newest MMS
            }

            var address = MmsReader.GetAddress(context, mms.Id, mms.IsOutgoing);
            // The conversation list only shows a text snippet (BuildPreviewText below uses the
            // attachment's Kind/FileName, never DataUri), so skip decoding image/GIF bytes here —
            // that cost is only worth paying once a thread is actually opened.
            var (body, attachments) = MmsReader.GetContent(context, mms.Id, includeAttachmentData: false);

            latestByThread[mms.ThreadId] = new ThreadLatestMessage
            {
                ThreadId = mms.ThreadId,
                DateMillis = dateMillis,
                Address = address,
                Body = BuildPreviewText(body, attachments),
                IsUnread = !mms.IsRead
            };
        }
    }

    // Mirrors how every mainstream messaging app previews an image/video-only MMS: since
    // there's no text part to show, fall back to a short label describing what was sent.
    private static string BuildPreviewText(string body, List<MessageAttachment> attachments)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        if (attachments.Count == 0)
        {
            return string.Empty;
        }

        return attachments[0].Kind switch
        {
            AttachmentKind.Image => "📷 Photo",
            AttachmentKind.Gif => "GIF",
            AttachmentKind.Video => "🎬 Video",
            _ => $"📎 {attachments[0].FileName}"
        };
    }
}
