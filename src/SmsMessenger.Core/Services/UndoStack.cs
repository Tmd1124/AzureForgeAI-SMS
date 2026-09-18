namespace SmsMessenger.Core.Services;

public class UndoStack : IUndoStack
{
    private readonly Stack<IUndoableAction> _actions = new();

    public bool HasActions => _actions.Count > 0;

    public void Push(IUndoableAction action) => _actions.Push(action);

    public async Task<string?> UndoAsync()
    {
        if (_actions.Count == 0)
        {
            return null;
        }

        var action = _actions.Pop();
        await action.UndoAsync();
        return action.Description;
    }
}
