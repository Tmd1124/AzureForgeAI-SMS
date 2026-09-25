using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Services;

public class MuteUndoAction : IUndoableAction
{
    private readonly long _threadId;
    private readonly IMuteRepository _muteRepository;

    public MuteUndoAction(long threadId, IMuteRepository muteRepository)
    {
        _threadId = threadId;
        _muteRepository = muteRepository;
    }

    public string Description => "Muted a conversation";

    public Task UndoAsync() => _muteRepository.UnmuteAsync(_threadId);
}
