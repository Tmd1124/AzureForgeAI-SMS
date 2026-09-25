using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidAlarmManager = global::Android.App.AlarmManager;
using AndroidAlarmType = global::Android.App.AlarmType;
using AndroidPendingIntent = global::Android.App.PendingIntent;
using AndroidPendingIntentFlags = global::Android.App.PendingIntentFlags;
using AndroidIntent = global::Android.Content.Intent;
using AndroidContext = global::Android.Content.Context;

namespace ForgeLinkSms.Platforms.Android;

// Same inexact-but-Doze-safe alarm approach as MessageSchedulerService; a snooze waking a
// few minutes late is fine, and the list also wakes anything overdue whenever it loads.
public class SnoozeAlarmScheduler : ISnoozeAlarmScheduler
{
    public const string ThreadIdExtra = "snoozed_thread_id";

    public void Arm(long threadId, DateTimeOffset untilUtc)
    {
        var context = AndroidApp.Context;
        var alarmManager = (AndroidAlarmManager)context.GetSystemService(AndroidContext.AlarmService)!;
        alarmManager.SetAndAllowWhileIdle(AndroidAlarmType.RtcWakeup, untilUtc.ToUnixTimeMilliseconds(), PendingIntentFor(context, threadId));
    }

    public void Disarm(long threadId)
    {
        var context = AndroidApp.Context;
        var alarmManager = (AndroidAlarmManager)context.GetSystemService(AndroidContext.AlarmService)!;
        alarmManager.Cancel(PendingIntentFor(context, threadId));
    }

    private static AndroidPendingIntent PendingIntentFor(AndroidContext context, long threadId)
    {
        var intent = new AndroidIntent(context, typeof(SnoozeWakeReceiver)).SetPackage(context.PackageName);
        intent.PutExtra(ThreadIdExtra, threadId);
        return AndroidPendingIntent.GetBroadcast(context, (int)threadId, intent, AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.UpdateCurrent)!;
    }
}
