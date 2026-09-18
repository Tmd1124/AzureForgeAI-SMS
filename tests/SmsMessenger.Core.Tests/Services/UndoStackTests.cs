using Moq;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.Tests.Services;

public class UndoStackTests
{
    [Fact]
    public void HasActions_is_false_when_empty()
    {
        var stack = new UndoStack();

        Assert.False(stack.HasActions);
    }

    [Fact]
    public async Task UndoAsync_returns_null_when_empty()
    {
        var stack = new UndoStack();

        var result = await stack.UndoAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task UndoAsync_calls_UndoAsync_on_the_most_recently_pushed_action()
    {
        var stack = new UndoStack();
        var first = new Mock<IUndoableAction>();
        first.Setup(a => a.Description).Returns("first");
        var second = new Mock<IUndoableAction>();
        second.Setup(a => a.Description).Returns("second");
        stack.Push(first.Object);
        stack.Push(second.Object);

        var result = await stack.UndoAsync();

        Assert.Equal("second", result);
        second.Verify(a => a.UndoAsync(), Times.Once);
        first.Verify(a => a.UndoAsync(), Times.Never);
    }

    [Fact]
    public async Task UndoAsync_pops_actions_in_LIFO_order_across_multiple_calls()
    {
        var stack = new UndoStack();
        var first = new Mock<IUndoableAction>();
        first.Setup(a => a.Description).Returns("first");
        var second = new Mock<IUndoableAction>();
        second.Setup(a => a.Description).Returns("second");
        stack.Push(first.Object);
        stack.Push(second.Object);

        await stack.UndoAsync();
        var result = await stack.UndoAsync();

        Assert.Equal("first", result);
        Assert.False(stack.HasActions);
    }
}
