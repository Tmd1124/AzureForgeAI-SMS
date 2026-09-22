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
        var trashRepository = MakeEmptyExpiredTrashRepository();
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231"),
            MakeThread(2, "5550148890")
        });
        var viewModel = MakeViewModel(trashRepository, threadService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.TrashedThreads);
        Assert.Equal(2, viewModel.TrashedThreads[0].Id);
    }

    [Fact]
    public async Task RestoreCommand_removes_the_thread_from_trash_and_reloads()
    {
        var trashRepository = MakeEmptyExpiredTrashRepository();
        trashRepository.SetupSequence(r => r.GetTrashedThreadIdsAsync())
            .ReturnsAsync(new List<long> { 2 })
            .ReturnsAsync(new List<long>());
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(2, "5550148890") });
        var viewModel = MakeViewModel(trashRepository, threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RestoreCommand.ExecuteAsync(2L);

        trashRepository.Verify(r => r.RestoreThreadAsync(2), Times.Once);
        Assert.Empty(viewModel.TrashedThreads);
    }

    [Fact]
    public async Task LoadCommand_permanently_deletes_threads_trashed_over_30_days_ago_before_loading()
    {
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.Setup(r => r.GetExpiredThreadIdsAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(new List<long> { 9 });
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 9, 2 });
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(9, "5550142231"),
            MakeThread(2, "5550148890")
        });
        var threadDeletionService = new Mock<IThreadDeletionService>();
        var favoriteRepository = new Mock<IFavoriteRepository>();
        var filterRepository = new Mock<IFilterRepository>();
        var viewModel = MakeViewModel(trashRepository, threadService, threadDeletionService, favoriteRepository, filterRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        threadDeletionService.Verify(s => s.DeleteThreadAsync(9), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(9), Times.Once);
        filterRepository.Verify(r => r.RemoveAllAssignmentsForThreadAsync(9), Times.Once);
        trashRepository.Verify(r => r.RestoreThreadAsync(9), Times.Once);
        threadDeletionService.Verify(s => s.DeleteThreadAsync(2), Times.Never);
        Assert.Single(viewModel.TrashedThreads);
        Assert.Equal(2, viewModel.TrashedThreads[0].Id);
    }

    [Fact]
    public async Task LoadCommand_does_not_touch_threads_trashed_within_the_last_30_days()
    {
        var trashRepository = MakeEmptyExpiredTrashRepository();
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(2, "5550148890") });
        var threadDeletionService = new Mock<IThreadDeletionService>();
        var viewModel = MakeViewModel(trashRepository, threadService, threadDeletionService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        threadDeletionService.Verify(s => s.DeleteThreadAsync(It.IsAny<long>()), Times.Never);
        Assert.Single(viewModel.TrashedThreads);
    }

    [Fact]
    public async Task DeleteThreadsCommand_permanently_deletes_every_selected_thread_and_reloads()
    {
        var trashRepository = MakeEmptyExpiredTrashRepository();
        trashRepository.SetupSequence(r => r.GetTrashedThreadIdsAsync())
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
        var viewModel = MakeViewModel(trashRepository, threadService, threadDeletionService, favoriteRepository, filterRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        threadDeletionService.Verify(s => s.DeleteThreadAsync(1), Times.Once);
        threadDeletionService.Verify(s => s.DeleteThreadAsync(2), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(2), Times.Once);
        filterRepository.Verify(r => r.RemoveAllAssignmentsForThreadAsync(1), Times.Once);
        filterRepository.Verify(r => r.RemoveAllAssignmentsForThreadAsync(2), Times.Once);
        trashRepository.Verify(r => r.RestoreThreadAsync(1), Times.Once);
        trashRepository.Verify(r => r.RestoreThreadAsync(2), Times.Once);
        Assert.Empty(viewModel.TrashedThreads);
    }

    private static Mock<ITrashRepository> MakeEmptyExpiredTrashRepository()
    {
        var repository = new Mock<ITrashRepository>();
        repository.Setup(r => r.GetExpiredThreadIdsAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(new List<long>());
        return repository;
    }

    private static TrashViewModel MakeViewModel(
        Mock<ITrashRepository> trashRepository,
        Mock<IThreadService> threadService,
        Mock<IThreadDeletionService>? threadDeletionService = null,
        Mock<IFavoriteRepository>? favoriteRepository = null,
        Mock<IFilterRepository>? filterRepository = null) =>
        new(
            trashRepository.Object,
            threadService.Object,
            (threadDeletionService ?? new Mock<IThreadDeletionService>()).Object,
            (favoriteRepository ?? new Mock<IFavoriteRepository>()).Object,
            (filterRepository ?? new Mock<IFilterRepository>()).Object);
}
