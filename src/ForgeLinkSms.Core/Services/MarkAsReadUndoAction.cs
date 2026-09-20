namespace ForgeLinkSms.Core.Services;

public class MarkAsReadUndoAction : IUndoableAction
{
    private readonly IReadOnlyList<long> _threadIds;
    private readonly IMarkAsReadService _markAsReadService;

    public MarkAsReadUndoAction(IReadOnlyList<long> threadIds, IMarkAsReadService markAsReadService)
    {
        _threadIds = threadIds;
        _markAsReadService = markAsReadService;
    }

    public string Description => $"Marked {_threadIds.Count} conversation(s) as read";

    public Task UndoAsync() => _markAsReadService.MarkThreadsAsUnreadAsync(_threadIds);
}
