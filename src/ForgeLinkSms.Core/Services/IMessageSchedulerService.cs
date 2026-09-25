namespace ForgeLinkSms.Core.Services;

public interface IMessageSchedulerService
{
    Task<int> ScheduleAsync(string address, string body, DateTimeOffset sendAtUtc);

    /// Schedules one group message to everyone; threadId 0 finds or creates the group conversation when it sends.
    Task<int> ScheduleGroupAsync(long threadId, IReadOnlyList<string> addresses, string body, DateTimeOffset sendAtUtc);
    Task CancelAsync(int scheduledMessageId);

    /// Re-arms every still-pending scheduled message's alarm. Android clears all AlarmManager
    /// alarms on reboot, so this must run again after the device restarts (see BootCompletedReceiver).
    Task RescheduleAllPendingAsync();
}
