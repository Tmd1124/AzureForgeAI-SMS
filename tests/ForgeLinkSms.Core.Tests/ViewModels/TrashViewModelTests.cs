using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class TrashViewModelTests
{
    private static SmsThread MakeThread(long id, string address) => new()
    {
        Id = id,
        Address = address,
        DisplayName = null,
        LastMessageBody = "hi",
        LastMessageTimestamp = DateTimeOffset.UtcNow,
        UnreadCount = 0
    };

    [Fact]
    public async Task LoadCommand_only_includes_trashed_threads()
    {
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231"),
            MakeThread(2, "5550148890")
        });
        var viewModel = new TrashViewModel(trashRepository.Object, threadService.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.TrashedThreads);
        Assert.Equal(2, viewModel.TrashedThreads[0].Id);
    }

    [Fact]
    public async Task RestoreCommand_removes_the_thread_from_trash_and_reloads()
    {
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.SetupSequence(r => r.GetTrashedThreadIdsAsync())
            .ReturnsAsync(new List<long> { 2 })
            .ReturnsAsync(new List<long>());
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(2, "5550148890") });
        var viewModel = new TrashViewModel(trashRepository.Object, threadService.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RestoreCommand.ExecuteAsync(2L);

        trashRepository.Verify(r => r.RestoreThreadAsync(2), Times.Once);
        Assert.Empty(viewModel.TrashedThreads);
    }
}
