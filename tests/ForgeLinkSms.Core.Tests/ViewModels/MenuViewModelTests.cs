using Moq;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class MenuViewModelTests
{
    [Fact]
    public async Task MarkAllAsReadCommand_pushes_an_undo_action_and_reports_the_count()
    {
        var markAsRead = new Mock<IMarkAsReadService>();
        markAsRead.Setup(s => s.MarkAllAsReadAsync()).ReturnsAsync(new List<long> { 1, 2 });
        var undoStack = new Mock<IUndoStack>();
        var viewModel = new MenuViewModel(markAsRead.Object, undoStack.Object);

        await viewModel.MarkAllAsReadCommand.ExecuteAsync(null);

        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
        Assert.Equal("Marked 2 conversation(s) as read", viewModel.StatusMessage);
    }

    [Fact]
    public async Task MarkAllAsReadCommand_does_not_push_an_undo_action_when_nothing_was_unread()
    {
        var markAsRead = new Mock<IMarkAsReadService>();
        markAsRead.Setup(s => s.MarkAllAsReadAsync()).ReturnsAsync(new List<long>());
        var undoStack = new Mock<IUndoStack>();
        var viewModel = new MenuViewModel(markAsRead.Object, undoStack.Object);

        await viewModel.MarkAllAsReadCommand.ExecuteAsync(null);

        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Never);
        Assert.Equal("Nothing to mark as read", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UndoCommand_reports_what_was_undone()
    {
        var markAsRead = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        undoStack.Setup(s => s.UndoAsync()).ReturnsAsync("Trashed a conversation");
        var viewModel = new MenuViewModel(markAsRead.Object, undoStack.Object);

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Undid: Trashed a conversation", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UndoCommand_reports_nothing_to_undo_when_stack_is_empty()
    {
        var markAsRead = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        undoStack.Setup(s => s.UndoAsync()).ReturnsAsync((string?)null);
        var viewModel = new MenuViewModel(markAsRead.Object, undoStack.Object);

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Nothing to undo", viewModel.StatusMessage);
    }
}
