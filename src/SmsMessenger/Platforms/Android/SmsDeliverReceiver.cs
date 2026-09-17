using Android.App;
using Android.Content;

namespace SmsMessenger.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter(new[] { "android.provider.Telephony.SMS_DELIVER" })]
public class SmsDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        // Task 9 replaces this body with: parse PDUs via
        // Telephony.Sms.Intents.GetMessagesFromIntent(intent), write each
        // message into the Sms.Inbox content provider, then notify.
        global::Android.Util.Log.Debug("SmsMessenger", "SMS_DELIVER received (stub — Task 9 implements this)");
    }
}
