using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Services;

public class SnoozeService : ISnoozeService
{
    private readonly ISnoozeRepository _repository;
    private readonly ISnoozeAlarmScheduler _alarms;
    private readonly IMarkAsReadService _markAsReadService;

    public SnoozeService(ISnoozeRepository repository, ISnoozeAlarmScheduler alarms, IMarkAsReadService markAsReadService)
    {
        _repository = repository;
        _alarms = alarms;
        _markAsReadService = markAsReadService;
    }

    public async Task SnoozeAsync(long threadId, DateTimeOffset untilUtc)
    {
        await _repository.SnoozeThreadAsync(threadId, untilUtc);
        _alarms.Arm(threadId, untilUtc);
    }

    public async Task UnsnoozeAsync(long threadId)
    {
        _alarms.Disarm(threadId);
        await _repository.UnsnoozeThreadAsync(threadId);
    }

    // Returns false when the thread was no longer snoozed (e.g. already woken by a new message),
    // so callers don't post a "back from snooze" notification for it.
    public async Task<bool> WakeAsync(long threadId)
    {
        if (await _repository.GetAsync(threadId) is null)
        {
            return false;
        }

        await UnsnoozeAsync(threadId);
        await _markAsReadService.MarkThreadsAsUnreadAsync(new[] { threadId });
        return true;
    }

    // Catches snoozes whose alarm was late or lost (Doze, force-stop) the next time the list loads.
    public async Task WakeExpiredAsync(DateTimeOffset now)
    {
        foreach (var snooze in await _repository.GetAllAsync())
        {
            if (snooze.UntilUtc <= now)
            {
                await WakeAsync(snooze.ThreadId);
            }
        }
    }

    public async Task<IReadOnlyDictionary<long, DateTimeOffset>> GetSnoozedAsync() =>
        (await _repository.GetAllAsync()).ToDictionary(s => s.ThreadId, s => s.UntilUtc);

    public async Task RearmAllAsync()
    {
        foreach (var snooze in await _repository.GetAllAsync())
        {
            _alarms.Arm(snooze.ThreadId, snooze.UntilUtc);
        }
    }
}
