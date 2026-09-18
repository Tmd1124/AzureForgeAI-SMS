using Android.App;
using Android.Content;
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

        var values = new AndroidContentValues();
        values.Put("address", address);
        values.Put("body", body);
        values.Put("date", Java.Lang.JavaSystem.CurrentTimeMillis());
        values.Put("read", 0);
        context.ContentResolver!.Insert(AndroidTelephony.Sms.Inbox.ContentUri!, values);

        // Task 11 hooks INotificationService in here to raise a local
        // notification when the app isn't in the foreground.
    }
}
