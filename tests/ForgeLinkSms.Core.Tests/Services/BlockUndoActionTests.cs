using Moq;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class BlockUndoActionTests
{
    [Fact]
    public async Task UndoAsync_unblocks_the_number()
    {
        var blockService = new Mock<IContactBlockService>();
        var action = new BlockUndoAction("5550142231", blockService.Object);

        await action.UndoAsync();

        blockService.Verify(s => s.UnblockAsync("5550142231"), Times.Once);
    }

    [Fact]
    public void Description_is_human_readable()
    {
        var blockService = new Mock<IContactBlockService>();
        var action = new BlockUndoAction("5550142231", blockService.Object);

        Assert.Equal("Blocked a contact", action.Description);
    }
}
