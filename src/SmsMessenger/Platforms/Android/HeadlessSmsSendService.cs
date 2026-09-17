using Android.App;
using Android.Content;
using Android.OS;

namespace SmsMessenger.Platforms.Android;

[Service(Exported = true, Permission = "android.permission.SEND_RESPOND_VIA_MESSAGE")]
[IntentFilter(new[] { "android.intent.action.RESPOND_VIA_MESSAGE" }, Categories = new[] { Intent.CategoryDefault }, DataSchemes = new[] { "sms", "smsto" })]
public class HeadlessSmsSendService : IntentService
{
    public HeadlessSmsSendService() : base(nameof(HeadlessSmsSendService)) { }

    protected override void OnHandleIntent(Intent? intent)
    {
        // Task 10 replaces this body with: extract the recipient + reply
        // text from the intent and send it via ISmsService, without
        // opening any UI.
        global::Android.Util.Log.Debug("SmsMessenger", "RESPOND_VIA_MESSAGE received (stub — Task 10 implements this)");
    }
}
