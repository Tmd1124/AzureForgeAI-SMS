using SmsMessenger.Core.Data;

namespace SmsMessenger.Core.Services;

public class ArchiveUndoAction : IUndoableAction
{
    private readonly long _threadId;
    private readonly IArchiveRepository _archiveRepository;

    public ArchiveUndoAction(long threadId, IArchiveRepository archiveRepository)
    {
        _threadId = threadId;
        _archiveRepository = archiveRepository;
    }

    public string Description => "Archived a conversation";

    public Task UndoAsync() => _archiveRepository.UnarchiveThreadAsync(_threadId);
}
