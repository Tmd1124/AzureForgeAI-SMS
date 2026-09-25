using Moq;
using ForgeLinkSms.Core.ViewModels;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ComputerAccessViewModelTests
{
    private sealed class MemoryStore : IPairedBrowserStore
    {
        private readonly HashSet<string> _hashes = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task AddAsync(string tokenHash) { _hashes.Add(tokenHash); return Task.CompletedTask; }
        public Task<bool> ContainsAsync(string tokenHash) => Task.FromResult(_hashes.Contains(tokenHash));
        public Task<int> CountAsync() => Task.FromResult(_hashes.Count);
        public Task ClearAsync() { _hashes.Clear(); return Task.CompletedTask; }
    }

    private readonly Mock<IComputerAccessController> _controller = new();
    private readonly PairingService _pairing = new(new MemoryStore());

    [Fact]
    public async Task Turning_it_on_shows_the_address_and_code()
    {
        _controller.Setup(c => c.StartAsync()).ReturnsAsync(true).Callback(() =>
        {
            _controller.SetupGet(c => c.IsRunning).Returns(true);
            _controller.SetupGet(c => c.Address).Returns("http://192.168.1.220:8765");
        });
        var viewModel = new ComputerAccessViewModel(_controller.Object, _pairing);

        await viewModel.ToggleCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsRunning);
        Assert.Equal("http://192.168.1.220:8765", viewModel.Address);
        Assert.Equal(_pairing.Code, viewModel.Code);
        Assert.Null(viewModel.Message);
    }

    [Fact]
    public async Task Turning_it_on_without_wifi_explains_why()
    {
        _controller.Setup(c => c.StartAsync()).ReturnsAsync(false);
        var viewModel = new ComputerAccessViewModel(_controller.Object, _pairing);

        await viewModel.ToggleCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRunning);
        Assert.Equal("Connect your phone to Wi-Fi first.", viewModel.Message);
    }

    [Fact]
    public async Task Turning_it_off_stops_the_server()
    {
        _controller.SetupGet(c => c.IsRunning).Returns(true);
        var viewModel = new ComputerAccessViewModel(_controller.Object, _pairing);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.ToggleCommand.ExecuteAsync(null);

        _controller.Verify(c => c.StopAsync(), Times.Once);
    }

    [Fact]
    public async Task UnpairAll_resets_the_count()
    {
        await _pairing.PairAsync(_pairing.Code, "192.168.1.130");
        var viewModel = new ComputerAccessViewModel(_controller.Object, _pairing);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, viewModel.PairedCount);

        await viewModel.UnpairAllCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.PairedCount);
    }
}
