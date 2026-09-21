namespace ForgeLinkSms.Core.Services;

public interface IMessageSchedulerService
{
    Task<int> ScheduleAsync(string address, string body, DateTimeOffset sendAtUtc);
    Task CancelAsync(int scheduledMessageId);

    /// Re-arms every still-pending scheduled message's alarm. Android clears all AlarmManager
    /// alarms on reboot, so this must run again after the device restarts (see BootCompletedReceiver).
    Task RescheduleAllPendingAsync();
}
