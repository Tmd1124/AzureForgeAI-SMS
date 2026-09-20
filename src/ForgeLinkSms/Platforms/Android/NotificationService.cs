using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class NotificationService : INotificationService
{
    private const string ChannelId = "incoming_sms";
    private static int _notificationId;

    public void NotifyIncomingMessage(string fromDisplayName, string body, long threadId, string address)
    {
        var context = AndroidApp.Context;
        EnsureChannel(context);

        var notificationId = System.Threading.Interlocked.Increment(ref _notificationId);
        var route = $"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}";

        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        launchIntent.PutExtra("initial_route", route);
        // Each notification needs its own PendingIntent request code — reusing one code across
        // notifications makes Android collapse them into a single PendingIntent, so tapping an
        // older notification would open whichever thread's route was set most recently.
        var contentIntent = PendingIntent.GetActivity(context, notificationId, launchIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(fromDisplayName)
            .SetContentText(body)
            .SetSmallIcon(global::Android.Resource.Drawable.SymActionEmail)
            .SetAutoCancel(true)
            .SetContentIntent(contentIntent)
            .Build();

        NotificationManagerCompat.From(context).Notify(notificationId, notification);
    }

    private static void EnsureChannel(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is null)
        {
            var channel = new NotificationChannel(ChannelId, "Incoming Messages", NotificationImportance.High);
            manager.CreateNotificationChannel(channel);
        }
    }
}
