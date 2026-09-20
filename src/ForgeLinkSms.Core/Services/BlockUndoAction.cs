namespace ForgeLinkSms.Core.Services;

public class BlockUndoAction : IUndoableAction
{
    private readonly string _phoneNumber;
    private readonly IContactBlockService _blockService;

    public BlockUndoAction(string phoneNumber, IContactBlockService blockService)
    {
        _phoneNumber = phoneNumber;
        _blockService = blockService;
    }

    public string Description => "Blocked a contact";

    public Task UndoAsync() => _blockService.UnblockAsync(_phoneNumber);
}
