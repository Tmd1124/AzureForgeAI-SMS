using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidAlarmManager = global::Android.App.AlarmManager;
using AndroidAlarmType = global::Android.App.AlarmType;
using AndroidPendingIntent = global::Android.App.PendingIntent;
using AndroidPendingIntentFlags = global::Android.App.PendingIntentFlags;
using AndroidIntent = global::Android.Content.Intent;
using AndroidContext = global::Android.Content.Context;

namespace ForgeLinkSms.Platforms.Android;

public class MessageSchedulerService : IMessageSchedulerService
{
    public const string ScheduledMessageIdExtra = "scheduled_message_id";

    private readonly IScheduledMessageRepository _repository;

    public MessageSchedulerService(IScheduledMessageRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> ScheduleAsync(string address, string body, DateTimeOffset sendAtUtc)
    {
        var id = await _repository.AddAsync(new ScheduledMessage { Address = address, Body = body, SendAtUtc = sendAtUtc });
        Arm(id, sendAtUtc);
        return id;
    }

    public async Task CancelAsync(int scheduledMessageId)
    {
        Disarm(scheduledMessageId);
        await _repository.RemoveAsync(scheduledMessageId);
    }

    public async Task RescheduleAllPendingAsync()
    {
        foreach (var message in await _repository.GetAllAsync())
        {
            Arm(message.Id, message.SendAtUtc);
        }
    }

    // Approximate delivery is acceptable here (confirmed with the user), so this uses
    // SetAndAllowWhileIdle rather than the exact-alarm APIs — it still fires during Doze,
    // but needs no special "Alarms & reminders" permission from the user.
    private static void Arm(int id, DateTimeOffset sendAtUtc)
    {
        var context = AndroidApp.Context;
        var alarmManager = (AndroidAlarmManager)context.GetSystemService(AndroidContext.AlarmService)!;
        alarmManager.SetAndAllowWhileIdle(AndroidAlarmType.RtcWakeup, sendAtUtc.ToUnixTimeMilliseconds(), PendingIntentFor(context, id));
    }

    private static void Disarm(int id)
    {
        var context = AndroidApp.Context;
        var alarmManager = (AndroidAlarmManager)context.GetSystemService(AndroidContext.AlarmService)!;
        alarmManager.Cancel(PendingIntentFor(context, id));
    }

    // Broadcasting with only an action string is an *implicit* broadcast, which Android's
    // background-broadcast limits can silently drop before it reaches a manifest-declared
    // receiver — SetPackage makes it explicit so ScheduledMessageReceiver reliably gets it
    // (same fix already applied to SmsService's sent/delivered callbacks).
    private static AndroidPendingIntent PendingIntentFor(AndroidContext context, int id)
    {
        var intent = new AndroidIntent(context, typeof(ScheduledMessageReceiver)).SetPackage(context.PackageName);
        intent.PutExtra(ScheduledMessageIdExtra, id);
        return AndroidPendingIntent.GetBroadcast(context, id, intent, AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.UpdateCurrent)!;
    }
}
