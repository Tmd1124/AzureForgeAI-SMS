using Moq;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.Tests.Services;

public class BulkUndoActionTests
{
    [Fact]
    public async Task UndoAsync_undoes_every_inner_action()
    {
        var first = new Mock<IUndoableAction>();
        var second = new Mock<IUndoableAction>();
        var action = new BulkUndoAction(new[] { first.Object, second.Object }, "Archived 2 conversation(s)");

        await action.UndoAsync();

        first.Verify(a => a.UndoAsync(), Times.Once);
        second.Verify(a => a.UndoAsync(), Times.Once);
    }

    [Fact]
    public void Description_is_the_supplied_text()
    {
        var action = new BulkUndoAction(Array.Empty<IUndoableAction>(), "Archived 2 conversation(s)");

        Assert.Equal("Archived 2 conversation(s)", action.Description);
    }
}
