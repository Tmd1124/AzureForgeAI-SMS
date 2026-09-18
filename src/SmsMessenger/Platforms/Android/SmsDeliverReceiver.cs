using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.Utils;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace SmsMessenger.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter(new[] { "android.provider.Telephony.SMS_DELIVER" })]
public class SmsDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        var messages = AndroidTelephony.Sms.Intents.GetMessagesFromIntent(intent);
        if (messages is null || messages.Length == 0)
        {
            return;
        }

        var address = messages[0]!.OriginatingAddress ?? string.Empty;
        var body = string.Concat(messages.Select(m => m!.MessageBody));

        var services = MauiApplication.Current.Services;
        var blockService = services.GetRequiredService<IContactBlockService>();
        var normalizedAddress = PhoneNumberFormatter.ToComparableDigits(address);
        if (blockService.IsBlockedAsync(normalizedAddress).GetAwaiter().GetResult())
        {
            return;
        }

        var values = new AndroidContentValues();
        values.Put("address", address);
        values.Put("body", body);
        values.Put("date", Java.Lang.JavaSystem.CurrentTimeMillis());
        values.Put("read", 0);
        var insertedUri = context.ContentResolver!.Insert(AndroidTelephony.Sms.Inbox.ContentUri!, values);

        var threadId = 0L;
        if (insertedUri is not null)
        {
            using var threadCursor = context.ContentResolver!.Query(insertedUri, new[] { "thread_id" }, null, null, null);
            if (threadCursor is not null && threadCursor.MoveToFirst())
            {
                threadId = threadCursor.GetLong(threadCursor.GetColumnIndexOrThrow("thread_id"));
            }
        }

        var contactService = services.GetRequiredService<IContactService>();
        var contact = contactService.LookupAsync(address).GetAwaiter().GetResult();

        var notificationService = services.GetRequiredService<INotificationService>();
        notificationService.NotifyIncomingMessage(contact?.DisplayName ?? address, body, threadId, address);
    }
}
