using ForgeLinkSms.Core.Services;
using SmsMessage = ForgeLinkSms.Core.Models.SmsMessage;
using SmsMessageStatus = ForgeLinkSms.Core.Models.SmsMessageStatus;
using AndroidApp = global::Android.App.Application;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidSmsManager = global::Android.Telephony.SmsManager;
using AndroidPendingIntent = global::Android.App.PendingIntent;
using AndroidPendingIntentFlags = global::Android.App.PendingIntentFlags;
using AndroidIntent = global::Android.Content.Intent;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace ForgeLinkSms.Platforms.Android;

public class SmsService : ISmsService
{
    public const string SentAction = "ForgeLinkSms.SMS_SENT";
    public const string DeliveredAction = "ForgeLinkSms.SMS_DELIVERED";

    // Total raw (pre-base64) attachment bytes allowed to inline across one page's worth of MMS
    // messages. 20MB raw becomes well under 30MB of base64 text plus JSON structure — safely
    // under the WebView's render-batch limit (observed failing around ~160MB) even accounting
    // for the rest of that page's non-attachment content.
    private const long PageAttachmentByteBudget = 20 * 1024 * 1024;

    // The body below is synchronous, blocking ContentResolver work — reading every MMS part in
    // a thread (and base64-encoding each image/GIF attachment) can take seconds for a media-heavy
    // conversation. Blazor Hybrid page lifecycle callbacks run on the UI thread by default, so
    // without Task.Run this call freezes touch input for as long as it takes, which is exactly
    // the kind of stall that trips Android's ANR watchdog (matches ThreadService's contact-lookup
    // backgrounding, done for the same reason).
    public Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId, DateTimeOffset? beforeTimestamp, int pageSize) =>
        Task.Run(() => GetMessagesCore(threadId, beforeTimestamp, pageSize, ascending: false));

    // The downward counterpart used to catch a trimmed loaded window back up to the thread's
    // true latest message: same merge/hydrate logic as GetMessagesAsync, just walking forward
    // in time from a cursor instead of backward.
    public Task<IReadOnlyList<SmsMessage>> GetNewerMessagesAsync(long threadId, DateTimeOffset afterTimestamp, int pageSize) =>
        Task.Run(() => GetMessagesCore(threadId, afterTimestamp, pageSize, ascending: true));

    private static IReadOnlyList<SmsMessage> GetMessagesCore(long threadId, DateTimeOffset? cursor, int pageSize, bool ascending)
    {
        var context = AndroidApp.Context;

        var smsCandidates = QuerySmsPage(context, threadId, cursor, pageSize, ascending);

        // MMS summaries (thread_id/date/read/box only, no part reads yet) are cheap regardless
        // of how many exist, so it's fine to read the full set and filter/cap here rather than
        // teaching MmsReader about paging too.
        var mmsCandidates = MmsReader.QueryAll(context, threadId)
            .Where(m => cursor is null || (ascending ? m.Date > cursor : m.Date < cursor));

        // Merge both sources by recency and keep only the closest `pageSize` overall (newest,
        // when paging backward; oldest, when catching up forward). This is what bounds how many
        // (expensive) MMS attachments get decoded per page — some conversations run to thousands
        // of MMS messages with multi-megabyte photos/videos, and decoding all of them just to
        // open the thread made the page take seconds, or in the worst case never finish.
        var merged = smsCandidates
            .Select(m => (Timestamp: m.Timestamp, Sms: (SmsMessage?)m, Mms: (MmsReader.MmsSummary?)null))
            .Concat(mmsCandidates.Select(m => (Timestamp: m.Date, Sms: (SmsMessage?)null, Mms: (MmsReader.MmsSummary?)m)));
        var page = (ascending ? merged.OrderBy(x => x.Timestamp) : merged.OrderByDescending(x => x.Timestamp))
            .Take(pageSize)
            .ToList();

        // Reading each MMS's parts (and decoding any image/GIF attachment) is the slow part of
        // opening a thread. Parallel.For fans these out across the thread pool instead of reading
        // them one at a time — this method already runs off the UI thread (see GetMessagesAsync
        // above), so it's safe to block here doing it.
        var mmsToHydrate = page.Where(x => x.Mms is not null).Select(x => x.Mms!).ToList();
        var hydratedMms = new SmsMessage[mmsToHydrate.Count];

        // The per-attachment size cap alone doesn't bound a page's total payload: a run of many
        // photos each just under that cap can still add up to a render batch the WebView's JSON
        // writer refuses to serialize (observed: a single 50-message page pushed one batch past
        // 160MB and crashed the thread view). This budget is shared across the whole page's
        // Parallel.For hydration so the combined inlined bytes for one page stay bounded — once
        // spent, remaining images in the page fall back to the file-chip UI instead of inlining.
        var attachmentBudget = new MmsReader.AttachmentBudget(PageAttachmentByteBudget);
        Parallel.For(0, mmsToHydrate.Count, i =>
        {
            var mms = mmsToHydrate[i];
            var address = MmsReader.GetAddress(context, mms.Id, mms.IsOutgoing);
            var (body, attachments) = MmsReader.GetContent(context, mms.Id, includeAttachmentData: true, attachmentBudget);

            hydratedMms[i] = new SmsMessage
            {
                Id = mms.Id,
                ThreadId = mms.ThreadId,
                Address = address,
                Body = body,
                Timestamp = mms.Date,
                IsOutgoing = mms.IsOutgoing,
                Status = mms.IsOutgoing ? SmsMessageStatus.Sent : SmsMessageStatus.Delivered,
                Attachments = attachments
            };
        });

        var mmsIndex = 0;
        var results = new List<SmsMessage>(page.Count);
        foreach (var item in page)
        {
            results.Add(item.Sms ?? hydratedMms[mmsIndex++]);
        }

        return results;
    }

    private static List<SmsMessage> QuerySmsPage(global::Android.Content.Context context, long threadId, DateTimeOffset? pagingCursor, int pageSize, bool ascending)
    {
        var results = new List<SmsMessage>();
        var projection = new[] { "_id", "thread_id", "address", "body", "date", "type", "status" };

        // Unfiltered, this table also returns draft/outbox/failed/queued placeholder rows (type
        // 3-6) that Android leaves sitting in the provider — one such row with no date ever set
        // surfaced as a fake "message" pinned at the very start of a real thread's history.
        // Restricting to inbox/sent (1/2) matches MmsReader.QueryAll's existing msg_box filter,
        // so only messages that were actually received or sent show up.
        var inbox = (int)global::Android.Provider.SmsMessageType.Inbox;
        var sent = (int)global::Android.Provider.SmsMessageType.Sent;
        var comparisonOp = ascending ? ">" : "<";
        var selection = pagingCursor is null
            ? "thread_id = ? AND (type = ? OR type = ?)"
            : $"thread_id = ? AND (type = ? OR type = ?) AND date {comparisonOp} ?";
        var args = pagingCursor is null
            ? new[] { threadId.ToString(), inbox.ToString(), sent.ToString() }
            : new[] { threadId.ToString(), inbox.ToString(), sent.ToString(), pagingCursor.Value.ToUnixTimeMilliseconds().ToString() };

        // Android's SMS provider (like most SQLite-backed platform ContentProviders) accepts a
        // raw SQL LIMIT appended to sortOrder — there's no separate paging parameter on
        // ContentResolver.Query, so this is the standard way third-party SMS apps page this table.
        var sortOrder = (ascending ? "date ASC LIMIT " : "date DESC LIMIT ") + pageSize;

        using var cursor = context.ContentResolver!.Query(AndroidTelephony.Sms.ContentUri!, projection, selection, args, sortOrder);
        if (cursor is null)
        {
            return results;
        }

        var idIdx = cursor.GetColumnIndexOrThrow("_id");
        var threadIdx = cursor.GetColumnIndexOrThrow("thread_id");
        var addressIdx = cursor.GetColumnIndexOrThrow("address");
        var bodyIdx = cursor.GetColumnIndexOrThrow("body");
        var dateIdx = cursor.GetColumnIndexOrThrow("date");
        var typeIdx = cursor.GetColumnIndexOrThrow("type");

        while (cursor.MoveToNext())
        {
            // type: 1 = inbox (incoming), 2 = sent (outgoing). See
            // Telephony.TextBasedSmsColumns.MessageTypeInbox / .MessageTypeSent.
            var type = cursor.GetInt(typeIdx);
            var isOutgoing = type == (int)global::Android.Provider.SmsMessageType.Sent;

            results.Add(new SmsMessage
            {
                Id = cursor.GetLong(idIdx),
                ThreadId = cursor.GetLong(threadIdx),
                Address = cursor.GetString(addressIdx) ?? string.Empty,
                Body = cursor.GetString(bodyIdx) ?? string.Empty,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(dateIdx)),
                IsOutgoing = isOutgoing,
                // A freshly-read Sent row can't distinguish Sent from
                // Delivered by content alone; DeliveryStatusReceiver
                // (Step 7) logs the delivery PendingIntent firing, so
                // this initial read reports Sent for outgoing rows.
                Status = isOutgoing ? SmsMessageStatus.Sent : SmsMessageStatus.Delivered
            });
        }

        return results;
    }

    public Task SendAsync(string address, string body)
    {
        var context = AndroidApp.Context;
        var smsManager = AndroidSmsManager.Default!;

        // Intent(action) alone is an *implicit* broadcast, which Android's
        // background-broadcast limits (and FLAG_EXCLUDE_STOPPED_PACKAGES,
        // on by default) can silently drop before it ever reaches a
        // manifest-declared receiver (confirmed via dumpsys activity
        // broadcasts: dispatchClockTime stayed at epoch and terminalCount
        // was 0 without this). Setting the package makes it an explicit
        // broadcast targeted at this app, which manifest receivers do
        // reliably receive.
        var sentIntent = new AndroidIntent(SentAction).SetPackage(context.PackageName);
        var deliveredIntent = new AndroidIntent(DeliveredAction).SetPackage(context.PackageName);
        var sentPending = AndroidPendingIntent.GetBroadcast(context, 0, sentIntent, AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.UpdateCurrent)!;
        var deliveredPending = AndroidPendingIntent.GetBroadcast(context, 0, deliveredIntent, AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.UpdateCurrent)!;

        var parts = smsManager.DivideMessage(body);
        if (parts.Count > 1)
        {
            var sentIntents = new List<AndroidPendingIntent>();
            var deliveredIntents = new List<AndroidPendingIntent>();
            for (var i = 0; i < parts.Count; i++)
            {
                sentIntents.Add(sentPending);
                deliveredIntents.Add(deliveredPending);
            }
            smsManager.SendMultipartTextMessage(address, null, parts, sentIntents, deliveredIntents);
        }
        else
        {
            smsManager.SendTextMessage(address, null, body, sentPending, deliveredPending);
        }

        // The default SMS app is responsible for writing its own outgoing
        // messages into the Sent provider — Android does not do this for us.
        var values = new AndroidContentValues();
        values.Put("address", address);
        values.Put("body", body);
        values.Put("date", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        values.Put("read", 1);
        context.ContentResolver!.Insert(AndroidTelephony.Sms.Sent.ContentUri!, values);

        return Task.CompletedTask;
    }
}
