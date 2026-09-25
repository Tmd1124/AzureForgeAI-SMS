using Android.App;
using Android.Content;

namespace ForgeLinkSms.Platforms.Android;

// Incoming picture and group messages (MMS) arrive here first as a small notification PDU; the
// actual message is downloaded and saved by MmsDownloader.
[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_WAP_PUSH")]
[IntentFilter(new[] { "android.provider.Telephony.WAP_PUSH_DELIVER" }, DataMimeType = "application/vnd.wap.mms-message")]
public class WapPushDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var pdu = intent?.GetByteArrayExtra("data");
        if (context is null || pdu is null)
        {
            return;
        }

        var pendingResult = GoAsync();
        Task.Run(() =>
        {
            try
            {
                MmsDownloader.Start(context, pdu);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("ForgeLinkSms", $"Starting MMS download failed: {ex}");
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
