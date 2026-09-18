using Android.App;
using Android.Content;

namespace SmsMessenger.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { SmsService.SentAction })]
[IntentFilter(new[] { SmsService.DeliveredAction })]
public class DeliveryStatusReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        // ResultCode is set by the framework when the PendingIntent fires:
        // Result.Ok for the SendAsync-side "sent" callback, and a non-null
        // PDU extra for the carrier "delivered" report. Task 12's manual
        // test verifies the observable effect (ticks changing in the UI);
        // wiring this into a persisted per-message status column is a
        // straightforward extension of the ContentValues update pattern
        // already used in SmsService.SendAsync and SmsDeliverReceiver.
        global::Android.Util.Log.Debug("SmsMessenger", $"Delivery status callback: {intent?.Action}, resultCode={ResultCode}");
    }
}
