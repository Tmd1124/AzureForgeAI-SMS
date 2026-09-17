using Android.App;
using Android.Content;
using Android.OS;

namespace SmsMessenger.Platforms.Android;

[Activity(Exported = true)]
[IntentFilter(new[] { Intent.ActionSendto }, Categories = new[] { Intent.CategoryDefault }, DataSchemes = new[] { "sms", "smsto" })]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeTypes = new[] { "text/plain" })]
public class ComposeSmsActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Task 10 replaces this body with: extract the recipient address
        // (Intent.Data, "smsto:<number>") and any prefilled body
        // (Intent.GetStringExtra(Intent.ExtraText)), then hand off to
        // MainActivity's Compose page with those values pre-populated.
        Finish();
    }
}
