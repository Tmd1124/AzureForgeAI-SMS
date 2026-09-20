using Android.App;
using Android.Content;
using Android.OS;

namespace ForgeLinkSms.Platforms.Android;

[Activity(Exported = true)]
[IntentFilter(new[] { Intent.ActionSendto }, Categories = new[] { Intent.CategoryDefault }, DataSchemes = new[] { "sms", "smsto" })]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeTypes = new[] { "text/plain" })]
public class ComposeSmsActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var address = Intent?.Data?.SchemeSpecificPart;
        // ACTION_SEND's body text isn't carried through "initial_route" yet — no code reads it back out on
        // the MainActivity/NavigationManager side, so only the recipient survives this hand-off for now.
        _ = Intent?.GetStringExtra(Intent.ExtraText);

        var route = $"/compose?add={Uri.EscapeDataString(address ?? string.Empty)}";
        var mainIntent = new Intent(this, typeof(MainActivity));
        mainIntent.PutExtra("initial_route", route);
        mainIntent.AddFlags(ActivityFlags.NewTask);
        StartActivity(mainIntent);
        Finish();
    }
}
