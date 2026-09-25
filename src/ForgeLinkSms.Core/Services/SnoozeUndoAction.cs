namespace ForgeLinkSms.Core.Services;

public class SnoozeUndoAction : IUndoableAction
{
    private readonly long _threadId;
    private readonly ISnoozeService _snoozeService;

    public SnoozeUndoAction(long threadId, ISnoozeService snoozeService)
    {
        _threadId = threadId;
        _snoozeService = snoozeService;
    }

    public string Description => "Snoozed a conversation";

    public Task UndoAsync() => _snoozeService.UnsnoozeAsync(_threadId);
}
