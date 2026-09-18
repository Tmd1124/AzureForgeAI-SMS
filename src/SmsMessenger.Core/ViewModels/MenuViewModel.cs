using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class MenuViewModel : ObservableObject
{
    private readonly IMarkAsReadService _markAsReadService;
    private readonly IUndoStack _undoStack;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public MenuViewModel(IMarkAsReadService markAsReadService, IUndoStack undoStack)
    {
        _markAsReadService = markAsReadService;
        _undoStack = undoStack;
    }

    [RelayCommand]
    private async Task MarkAllAsRead()
    {
        var threadIds = await _markAsReadService.MarkAllAsReadAsync();
        _undoStack.Push(new MarkAsReadUndoAction(threadIds, _markAsReadService));
        StatusMessage = $"Marked {threadIds.Count} conversation(s) as read";
    }

    [RelayCommand]
    private async Task Undo()
    {
        var description = await _undoStack.UndoAsync();
        StatusMessage = description is null ? "Nothing to undo" : $"Undid: {description}";
    }
}
