using Android.App;
using Android.Content;

namespace ForgeLinkSms.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_WAP_PUSH")]
[IntentFilter(new[] { "android.provider.Telephony.WAP_PUSH_DELIVER" }, DataMimeType = "application/vnd.wap.mms-message")]
public class WapPushDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        // Permanent no-op: MMS is out of scope for v1 (see Global
        // Constraints — no MMS/media in v1). This receiver exists solely
        // so Android's RoleManager considers this app eligible for the
        // default-SMS-app role, which requires handling WAP_PUSH_DELIVER
        // alongside SMS_DELIVER. If MMS is ever added in a later phase,
        // this is where that work starts.
        global::Android.Util.Log.Debug("ForgeLinkSms", "WAP_PUSH_DELIVER received (stub — MMS not in v1 scope)");
    }
}
