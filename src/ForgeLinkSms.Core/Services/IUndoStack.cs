namespace ForgeLinkSms.Core.Services;

public interface IUndoStack
{
    bool HasActions { get; }
    void Push(IUndoableAction action);
    Task<string?> UndoAsync();
}
