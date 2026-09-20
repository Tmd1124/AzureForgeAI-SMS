using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Services;

public class TrashUndoAction : IUndoableAction
{
    private readonly long _threadId;
    private readonly ITrashRepository _trashRepository;

    public TrashUndoAction(long threadId, ITrashRepository trashRepository)
    {
        _threadId = threadId;
        _trashRepository = trashRepository;
    }

    public string Description => "Trashed a conversation";

    public Task UndoAsync() => _trashRepository.RestoreThreadAsync(_threadId);
}
