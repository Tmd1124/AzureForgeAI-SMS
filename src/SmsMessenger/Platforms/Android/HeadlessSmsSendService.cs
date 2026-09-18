using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Platforms.Android;

[Service(Exported = true, Permission = "android.permission.SEND_RESPOND_VIA_MESSAGE")]
[IntentFilter(new[] { "android.intent.action.RESPOND_VIA_MESSAGE" }, Categories = new[] { Intent.CategoryDefault }, DataSchemes = new[] { "sms", "smsto" })]
public class HeadlessSmsSendService : IntentService
{
    public HeadlessSmsSendService() : base(nameof(HeadlessSmsSendService)) { }

    protected override void OnHandleIntent(Intent? intent)
    {
        var address = intent?.Data?.SchemeSpecificPart;
        var body = intent?.GetStringExtra("android.intent.extra.TEXT");

        if (!string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(body))
        {
            var smsService = MauiApplication.Current.Services.GetRequiredService<ISmsService>();
            smsService.SendAsync(address, body).GetAwaiter().GetResult();
        }
    }
}
