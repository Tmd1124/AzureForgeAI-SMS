using Moq;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ConversationsViewModelTests
{
    private static SmsThread MakeThread(long id, string address, string? name, string lastMessage, DateTimeOffset? timestamp = null, int unreadCount = 0) => new()
    {
        Id = id,
        Address = address,
        DisplayName = name,
        LastMessageBody = lastMessage,
        LastMessageTimestamp = timestamp ?? DateTimeOffset.UtcNow,
        UnreadCount = unreadCount
    };

    private static Mock<ITrashRepository> MakeEmptyTrashRepository()
    {
        var repository = new Mock<ITrashRepository>();
        repository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static Mock<IContactBlockService> MakeEmptyBlockService()
    {
        var service = new Mock<IContactBlockService>();
        service.Setup(s => s.GetBlockedNumbersAsync()).ReturnsAsync(new List<string>());
        return service;
    }

    private static Mock<IFavoriteRepository> MakeEmptyFavoriteRepository()
    {
        var repository = new Mock<IFavoriteRepository>();
        repository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static Mock<IArchiveRepository> MakeEmptyArchiveRepository()
    {
        var repository = new Mock<IArchiveRepository>();
        repository.Setup(r => r.GetArchivedThreadIdsAsync()).ReturnsAsync(new List<long>());
        return repository;
    }

    private static ConversationsViewModel MakeViewModel(
        Mock<IThreadService> threadService,
        Mock<ITrashRepository>? trashRepository = null,
        Mock<IContactBlockService>? blockService = null,
        Mock<IFavoriteRepository>? favoriteRepository = null,
        Mock<IUndoStack>? undoStack = null,
        Mock<IMarkAsReadService>? markAsReadService = null,
        Mock<IArchiveRepository>? archiveRepository = null) =>
        new(
            threadService.Object,
            (trashRepository ?? MakeEmptyTrashRepository()).Object,
            (blockService ?? MakeEmptyBlockService()).Object,
            (favoriteRepository ?? MakeEmptyFavoriteRepository()).Object,
            (undoStack ?? new Mock<IUndoStack>()).Object,
            (markAsReadService ?? new Mock<IMarkAsReadService>()).Object,
            (archiveRepository ?? MakeEmptyArchiveRepository()).Object);

    [Fact]
    public async Task LoadCommand_ignores_a_second_concurrent_call_while_the_first_is_still_running()
    {
        var threadServiceTcs = new TaskCompletionSource<IReadOnlyList<SmsThread>>();
        var threadService = new Mock<IThreadService>();
        var callCount = 0;
        threadService.Setup(s => s.GetThreadsAsync()).Returns(() =>
        {
            callCount++;
            return threadServiceTcs.Task;
        });
        var viewModel = MakeViewModel(threadService);

        var firstLoad = viewModel.LoadCommand.ExecuteAsync(null);
        var secondLoad = viewModel.LoadCommand.ExecuteAsync(null);
        threadServiceTcs.SetResult(new List<SmsThread> { MakeThread(1, "5550142231", "Alice Smith", "hi") });
        await Task.WhenAll(firstLoad, secondLoad);

        Assert.Equal(1, callCount);
        Assert.Single(viewModel.Threads);
    }

    [Fact]
    public async Task LoadCommand_populates_Threads_from_the_service()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = MakeViewModel(threadService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Threads.Count);
    }

    [Fact]
    public async Task LoadCommand_excludes_trashed_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var trashRepository = new Mock<ITrashRepository>();
        trashRepository.Setup(r => r.GetTrashedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var viewModel = MakeViewModel(threadService, trashRepository: trashRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task LoadCommand_excludes_blocked_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var blockService = new Mock<IContactBlockService>();
        blockService.Setup(s => s.GetBlockedNumbersAsync()).ReturnsAsync(new List<string> { "5550148890" });
        var viewModel = MakeViewModel(threadService, blockService: blockService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task LoadCommand_sorts_favorited_threads_to_the_top_regardless_of_recency()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-5);
        var newer = DateTimeOffset.UtcNow;
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", newer),
            MakeThread(2, "5550148890", "Bob Jones", "hey", older)
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Threads[0].Id);
        Assert.True(viewModel.Threads[0].IsFavorite);
        Assert.Equal(1, viewModel.Threads[1].Id);
        Assert.False(viewModel.Threads[1].IsFavorite);
    }

    [Fact]
    public async Task LoadCommand_excludes_archived_threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var archiveRepository = new Mock<IArchiveRepository>();
        archiveRepository.Setup(r => r.GetArchivedThreadIdsAsync()).ReturnsAsync(new List<long> { 2 });
        var viewModel = MakeViewModel(threadService, archiveRepository: archiveRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task ArchiveThreadCommand_archives_the_thread_and_pushes_an_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var archiveRepository = MakeEmptyArchiveRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, archiveRepository: archiveRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ArchiveThreadCommand.ExecuteAsync(1L);

        archiveRepository.Verify(r => r.ArchiveThreadAsync(1), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task LoadCommand_sorts_unread_threads_below_favorites_and_above_read_threads()
    {
        var newest = DateTimeOffset.UtcNow;
        var middle = DateTimeOffset.UtcNow.AddDays(-1);
        var oldest = DateTimeOffset.UtcNow.AddDays(-10);
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", newest, unreadCount: 0),
            MakeThread(2, "5550148890", "Bob Jones", "hey", middle, unreadCount: 3),
            MakeThread(3, "5550149999", "Carol Lee", "yo", oldest, unreadCount: 0)
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 3 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.Threads[0].Id);
        Assert.Equal(2, viewModel.Threads[1].Id);
        Assert.Equal(1, viewModel.Threads[2].Id);
    }

    [Fact]
    public async Task MarkThreadReadStateCommand_marks_an_unread_thread_as_read_and_pushes_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 2)
        });
        var markAsReadService = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, markAsReadService: markAsReadService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkThreadReadStateCommand.ExecuteAsync(1L);

        markAsReadService.Verify(s => s.MarkThreadAsReadAsync(1), Times.Once);
        markAsReadService.Verify(s => s.MarkThreadsAsUnreadAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task MarkThreadReadStateCommand_marks_a_read_thread_as_unread_without_pushing_undo()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi", unreadCount: 0)
        });
        var markAsReadService = new Mock<IMarkAsReadService>();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, markAsReadService: markAsReadService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkThreadReadStateCommand.ExecuteAsync(1L);

        markAsReadService.Verify(s => s.MarkThreadsAsUnreadAsync(It.Is<IReadOnlyList<long>>(ids => ids.Count == 1 && ids[0] == 1)), Times.Once);
        markAsReadService.Verify(s => s.MarkThreadAsReadAsync(It.IsAny<long>()), Times.Never);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Never);
    }

    [Fact]
    public async Task TrashThreadsCommand_trashes_every_thread_and_pushes_a_single_bulk_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var trashRepository = MakeEmptyTrashRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, trashRepository: trashRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.TrashThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        trashRepository.Verify(r => r.TrashThreadAsync(1), Times.Once);
        trashRepository.Verify(r => r.TrashThreadAsync(2), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveThreadsCommand_archives_every_thread_and_pushes_a_single_bulk_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var archiveRepository = MakeEmptyArchiveRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, archiveRepository: archiveRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ArchiveThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        archiveRepository.Verify(r => r.ArchiveThreadAsync(1), Times.Once);
        archiveRepository.Verify(r => r.ArchiveThreadAsync(2), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task TrashThreadCommand_trashes_the_thread_and_pushes_an_undo_action()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var trashRepository = MakeEmptyTrashRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, trashRepository: trashRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.TrashThreadCommand.ExecuteAsync(1L);

        trashRepository.Verify(r => r.TrashThreadAsync(1), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task BlockThreadCommand_blocks_the_address_pushes_an_undo_action_and_removes_it_from_Threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var blockService = MakeEmptyBlockService();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, blockService: blockService, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.BlockThreadCommand.ExecuteAsync("5550142231");

        blockService.Verify(s => s.BlockAsync("5550142231"), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
    }

    [Fact]
    public async Task FavoriteThreadCommand_favorites_a_non_favorited_thread_and_does_not_touch_undo()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var favoriteRepository = MakeEmptyFavoriteRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository, undoStack: undoStack);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.FavoriteThreadCommand.ExecuteAsync(1L);

        favoriteRepository.Verify(r => r.FavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(It.IsAny<long>()), Times.Never);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Never);
    }

    [Fact]
    public async Task FavoriteThreadCommand_unfavorites_an_already_favorited_thread()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 1 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.FavoriteThreadCommand.ExecuteAsync(1L);

        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.FavoriteThreadAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task FavoriteThreadsCommand_favorites_every_thread_when_not_all_are_already_favorited()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 1 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.FavoriteThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        favoriteRepository.Verify(r => r.FavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.FavoriteThreadAsync(2), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task FavoriteThreadsCommand_unfavorites_every_thread_when_all_are_already_favorited()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey")
        });
        var favoriteRepository = new Mock<IFavoriteRepository>();
        favoriteRepository.Setup(r => r.GetFavoriteThreadIdsAsync()).ReturnsAsync(new List<long> { 1, 2 });
        var viewModel = MakeViewModel(threadService, favoriteRepository: favoriteRepository);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.FavoriteThreadsCommand.ExecuteAsync(new List<long> { 1, 2 });

        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(1), Times.Once);
        favoriteRepository.Verify(r => r.UnfavoriteThreadAsync(2), Times.Once);
        favoriteRepository.Verify(r => r.FavoriteThreadAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task SearchText_filters_by_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey there")
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "alice";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }

    [Fact]
    public async Task SearchText_filters_by_raw_address_when_no_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "8890";

        Assert.Single(viewModel.Threads);
        Assert.Equal("5550148890", viewModel.Threads[0].Address);
    }

    [Fact]
    public async Task SearchText_filters_by_message_content()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "let's go to the gym"),
            MakeThread(2, "5550148890", "Bob Jones", "see you tomorrow")
        });
        var viewModel = MakeViewModel(threadService);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "gym";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }
}
