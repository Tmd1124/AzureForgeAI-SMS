using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ArchivedViewModelTests
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
    public async Task LoadCommand_only_includes_archived_threads()
    {
        var archiveRepository = new Mock<IArchiveRepository>();
        archiveRepository.Setup(r => r.GetArchivedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231"),
            MakeThread(2, "5550148890")
        });
        var viewModel = new ArchivedViewModel(archiveRepository.Object, threadService.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.ArchivedThreads);
        Assert.Equal(2, viewModel.ArchivedThreads[0].Id);
    }

    [Fact]
    public async Task UnarchiveCommand_removes_the_thread_from_archive_and_reloads()
    {
        var archiveRepository = new Mock<IArchiveRepository>();
        archiveRepository.SetupSequence(r => r.GetArchivedThreadIdsAsync())
            .ReturnsAsync(new List<long> { 2 })
            .ReturnsAsync(new List<long>());
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(2, "5550148890") });
        var viewModel = new ArchivedViewModel(archiveRepository.Object, threadService.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.UnarchiveCommand.ExecuteAsync(2L);

        archiveRepository.Verify(r => r.UnarchiveThreadAsync(2), Times.Once);
        Assert.Empty(viewModel.ArchivedThreads);
    }
}
