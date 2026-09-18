using Moq;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class BlockedViewModelTests
{
    [Fact]
    public async Task LoadCommand_populates_BlockedNumbers()
    {
        var blockService = new Mock<IContactBlockService>();
        blockService.Setup(s => s.GetBlockedNumbersAsync()).ReturnsAsync(new List<string> { "5550142231" });
        var viewModel = new BlockedViewModel(blockService.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.BlockedNumbers);
        Assert.Equal("5550142231", viewModel.BlockedNumbers[0]);
    }

    [Fact]
    public async Task UnblockCommand_unblocks_and_reloads()
    {
        var blockService = new Mock<IContactBlockService>();
        blockService.SetupSequence(s => s.GetBlockedNumbersAsync())
            .ReturnsAsync(new List<string> { "5550142231" })
            .ReturnsAsync(new List<string>());
        var viewModel = new BlockedViewModel(blockService.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.UnblockCommand.ExecuteAsync("5550142231");

        blockService.Verify(s => s.UnblockAsync("5550142231"), Times.Once);
        Assert.Empty(viewModel.BlockedNumbers);
    }
}
