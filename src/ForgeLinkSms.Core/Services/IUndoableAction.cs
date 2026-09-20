namespace ForgeLinkSms.Core.Services;

public interface IUndoableAction
{
    string Description { get; }
    Task UndoAsync();
}
