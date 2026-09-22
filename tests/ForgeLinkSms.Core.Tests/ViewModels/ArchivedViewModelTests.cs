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
        var viewModel = MakeViewModel(archiveRepository, threadService);

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
        var viewModel = MakeViewModel(archiveRepository, threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.UnarchiveCommand.ExecuteAsync(2L);

        archiveRepository.Verify(r => r.UnarchiveThreadAsync(2), Times.Once);
        Assert.Empty(viewModel.ArchivedThreads);
    }

    [Fact]
    public async Task DeleteThreadsCommand_permanently_deletes_every_selected_thread_and_reloads()
    {
        var archiveRepository = new Mock<IArchiveRepository>();
        archiveRepository.SetupSequence(r => r.GetArchivedThreadIdsAsync())
            .ReturnsAsync(new List<long> { 1, 2 })
            .ReturnsAsync(new List<long>());
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231"),
            MakeThread(2, "5550148890")
        });
        var threadDeletionService = new Mock<IThreadDeletionService>();
        var favoriteRepository = new Mock<IFavoriteRepository>();
        var filterRepository = new Mock<IFilterRepository>();
        var viewModel = MakeViewModel(archiveRepository, threadService, threadDeletionService, favoriteRepository, filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        threadDeletionService.Verify(s => s.DeleteThreadAsync(1), Times.Once);
        threadDeletionService.Verify(s => s.DeleteThreadAsync(2), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(2), Times.Once);
        filterRepository.Verify(r => r.RemoveAllAssignmentsForThreadAsync(1), Times.Once);
        filterRepository.Verify(r => r.RemoveAllAssignmentsForThreadAsync(2), Times.Once);
        archiveRepository.Verify(r => r.UnarchiveThreadAsync(1), Times.Once);
        archiveRepository.Verify(r => r.UnarchiveThreadAsync(2), Times.Once);
        Assert.Empty(viewModel.ArchivedThreads);
    }

    private static ArchivedViewModel MakeViewModel(
        Mock<IArchiveRepository> archiveRepository,
        Mock<IThreadService> threadService,
        Mock<IThreadDeletionService>? threadDeletionService = null,
        Mock<IFavoriteRepository>? favoriteRepository = null,
        Mock<IFilterRepository>? filterRepository = null) =>
        new(
            archiveRepository.Object,
            threadService.Object,
            (threadDeletionService ?? new Mock<IThreadDeletionService>()).Object,
            (favoriteRepository ?? new Mock<IFavoriteRepository>()).Object,
            (filterRepository ?? new Mock<IFilterRepository>()).Object);
}
