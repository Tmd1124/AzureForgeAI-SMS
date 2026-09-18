using SmsMessenger.Core.Services;
using SmsMessage = SmsMessenger.Core.Models.SmsMessage;
using SmsMessageStatus = SmsMessenger.Core.Models.SmsMessageStatus;
using AndroidApp = global::Android.App.Application;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidSmsManager = global::Android.Telephony.SmsManager;
using AndroidPendingIntent = global::Android.App.PendingIntent;
using AndroidPendingIntentFlags = global::Android.App.PendingIntentFlags;
using AndroidIntent = global::Android.Content.Intent;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace SmsMessenger.Platforms.Android;

public class SmsService : ISmsService
{
    public const string SentAction = "SmsMessenger.SMS_SENT";
    public const string DeliveredAction = "SmsMessenger.SMS_DELIVERED";

    public Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId)
    {
        var context = AndroidApp.Context;
        var results = new List<SmsMessage>();
        var projection = new[] { "_id", "thread_id", "address", "body", "date", "type", "status" };

        using var cursor = context.ContentResolver!.Query(
            AndroidTelephony.Sms.ContentUri!, projection, "thread_id = ?", new[] { threadId.ToString() }, "date ASC");

        if (cursor is not null)
        {
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
        }

        return Task.FromResult<IReadOnlyList<SmsMessage>>(results);
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
