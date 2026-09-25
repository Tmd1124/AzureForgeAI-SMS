using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class SnoozeWakeReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var threadId = intent?.GetLongExtra(SnoozeAlarmScheduler.ThreadIdExtra, -1) ?? -1;
        if (threadId < 0)
        {
            return;
        }

        var pendingResult = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var services = MauiApplication.Current.Services;
                if (!await services.GetRequiredService<ISnoozeService>().WakeAsync(threadId))
                {
                    return;
                }

                var threads = await services.GetRequiredService<IThreadService>().GetThreadsAsync();
                var thread = threads.FirstOrDefault(t => t.Id == threadId);
                if (thread is not null)
                {
                    services.GetRequiredService<INotificationService>()
                        .NotifyIncomingMessage(thread.DisplayNameOrAddress, $"⏰ Back from snooze: {thread.PreviewText}", thread.Id, thread.Address);
                }
                services.GetRequiredService<IIncomingMessageNotifier>().NotifyMessageReceived(threadId);
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
