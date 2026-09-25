namespace ForgeLinkSms.Core.Services;

public interface ISnoozeAlarmScheduler
{
    void Arm(long threadId, DateTimeOffset untilUtc);
    void Disarm(long threadId);
}
