using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Services;

public class AllowSenderUndoAction : IUndoableAction
{
    private readonly string _normalizedAddress;
    private readonly IAllowedSenderRepository _repository;

    public AllowSenderUndoAction(string normalizedAddress, IAllowedSenderRepository repository)
    {
        _normalizedAddress = normalizedAddress;
        _repository = repository;
    }

    public string Description => "Allowed a sender";

    public Task UndoAsync() => _repository.DisallowAsync(_normalizedAddress);
}
