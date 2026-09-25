using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

// Handles the Reply and Mark as read buttons on message notifications.
[BroadcastReceiver(Enabled = true, Exported = false)]
public class NotificationActionReceiver : BroadcastReceiver
{
    public const string ReplyAction = "forgelink.notification.REPLY";
    public const string MarkReadAction = "forgelink.notification.MARK_READ";
    public const string ReplyTextKey = "forgelink_reply_text";
    private const string ThreadIdExtra = "forgelink_thread_id";
    private const string AddressExtra = "forgelink_address";
    private const string NotificationIdExtra = "forgelink_notification_id";

    public static Intent CreateIntent(Context context, string action, long threadId, string address, int notificationId)
    {
        var intent = new Intent(context, typeof(NotificationActionReceiver)).SetAction(action).SetPackage(context.PackageName);
        intent.PutExtra(ThreadIdExtra, threadId);
        intent.PutExtra(AddressExtra, address);
        intent.PutExtra(NotificationIdExtra, notificationId);
        return intent;
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        var threadId = intent.GetLongExtra(ThreadIdExtra, 0);
        var address = intent.GetStringExtra(AddressExtra) ?? string.Empty;
        var notificationId = intent.GetIntExtra(NotificationIdExtra, 0);
        var replyText = AndroidX.Core.App.RemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(ReplyTextKey)?.ToString();
        var action = intent.Action;

        var pendingResult = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                var services = MauiApplication.Current.Services;
                if (action == ReplyAction && !string.IsNullOrWhiteSpace(replyText))
                {
                    var sender = new ConversationReplySender(
                        services.GetRequiredService<ISmsService>(),
                        services.GetRequiredService<IThreadService>(),
                        services.GetRequiredService<IMarkAsReadService>());
                    await sender.SendAsync(threadId, address, replyText);
                }
                else if (action == MarkReadAction)
                {
                    await services.GetRequiredService<IMarkAsReadService>().MarkThreadAsReadAsync(threadId);
                }

                // Clearing the notification also ends the "sending" spinner Android shows after a reply.
                NotificationManagerCompat.From(context).Cancel(notificationId);
                services.GetRequiredService<IIncomingMessageNotifier>().NotifyMessageReceived(threadId);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("ForgeLinkSms", $"Notification action {action} failed: {ex}");
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
