namespace SmsMessenger.Core.Services;

public class BulkUndoAction : IUndoableAction
{
    private readonly IReadOnlyList<IUndoableAction> _actions;

    public BulkUndoAction(IReadOnlyList<IUndoableAction> actions, string description)
    {
        _actions = actions;
        Description = description;
    }

    public string Description { get; }

    public async Task UndoAsync()
    {
        foreach (var action in _actions)
        {
            await action.UndoAsync();
        }
    }
}
