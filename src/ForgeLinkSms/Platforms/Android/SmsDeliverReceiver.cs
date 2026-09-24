using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace ForgeLinkSms.Platforms.Android;

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

        // OnReceive runs on the main thread, and Android only allows a BroadcastReceiver a few
        // seconds to return from it. The block-list check, SMS insert/query, and contact lookup
        // below are all blocking ContentResolver calls; running them here synchronously could
        // freeze the whole app — a real ANR — whenever those content providers were even briefly
        // contended (e.g. the conversations list doing its own batch of contact lookups at the
        // same moment). GoAsync() lets OnReceive return immediately while a background thread
        // does the actual work, and the system knows the broadcast isn't finished until
        // pendingResult.Finish() is called.
        var pendingResult = GoAsync();
        Task.Run(() =>
        {
            try
            {
                HandleMessage(context, address, body);
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }

    private static void HandleMessage(Context context, string address, string body)
    {
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

        if (threadId != 0L)
        {
            // A reply to an archived/trashed conversation means the user is actively back in
            // touch with it — surface it in the main list again rather than leaving a new,
            // unread message hidden in Archive/Trash where it's easy to miss. The message row
            // itself was already inserted with read=0 above, so the thread's unread state is
            // already correct once it's back in the main list.
            var archiveRepository = services.GetRequiredService<IArchiveRepository>();
            if (archiveRepository.IsArchivedAsync(threadId).GetAwaiter().GetResult())
            {
                archiveRepository.UnarchiveThreadAsync(threadId).GetAwaiter().GetResult();
            }

            var trashRepository = services.GetRequiredService<ITrashRepository>();
            if (trashRepository.IsTrashedAsync(threadId).GetAwaiter().GetResult())
            {
                trashRepository.RestoreThreadAsync(threadId).GetAwaiter().GetResult();
            }

            services.GetRequiredService<IIncomingMessageNotifier>().NotifyMessageReceived(threadId);
        }

        var contactService = services.GetRequiredService<IContactService>();
        var contact = contactService.LookupAsync(address).GetAwaiter().GetResult();

        var notificationService = services.GetRequiredService<INotificationService>();
        notificationService.NotifyIncomingMessage(contact?.DisplayName ?? address, body, threadId, address);
    }
}
