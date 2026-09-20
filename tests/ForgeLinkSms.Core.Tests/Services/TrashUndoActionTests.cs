using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class TrashUndoActionTests
{
    [Fact]
    public async Task UndoAsync_restores_the_trashed_thread()
    {
        var repository = new Mock<ITrashRepository>();
        var action = new TrashUndoAction(42, repository.Object);

        await action.UndoAsync();

        repository.Verify(r => r.RestoreThreadAsync(42), Times.Once);
    }

    [Fact]
    public void Description_is_human_readable()
    {
        var repository = new Mock<ITrashRepository>();
        var action = new TrashUndoAction(42, repository.Object);

        Assert.Equal("Trashed a conversation", action.Description);
    }
}
