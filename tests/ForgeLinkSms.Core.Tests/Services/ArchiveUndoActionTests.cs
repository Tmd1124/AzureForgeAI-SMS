using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class ArchiveUndoActionTests
{
    [Fact]
    public async Task UndoAsync_unarchives_the_thread()
    {
        var repository = new Mock<IArchiveRepository>();
        var action = new ArchiveUndoAction(42, repository.Object);

        await action.UndoAsync();

        repository.Verify(r => r.UnarchiveThreadAsync(42), Times.Once);
    }

    [Fact]
    public void Description_is_human_readable()
    {
        var repository = new Mock<IArchiveRepository>();
        var action = new ArchiveUndoAction(42, repository.Object);

        Assert.Equal("Archived a conversation", action.Description);
    }
}
