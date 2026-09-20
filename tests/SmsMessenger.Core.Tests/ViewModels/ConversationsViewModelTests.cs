using Moq;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ConversationsViewModelTests
{
    private static SmsThread MakeThread(long id, string address, string? name, string lastMessage, DateTimeOffset? timestamp = null) => new()
    {
        Id = id,
        Address = address,
        DisplayName = name,
        LastMessageBody = lastMessage,
        LastMessageTimestamp = timestamp ?? DateTimeOffset.UtcNow,
        UnreadCount = 0
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

    private static ConversationsViewModel MakeViewModel(
        Mock<IThreadService> threadService,
        Mock<ITrashRepository>? trashRepository = null,
        Mock<IContactBlockService>? blockService = null,
        Mock<IFavoriteRepository>? favoriteRepository = null,
        Mock<IUndoStack>? undoStack = null) =>
        new(
            threadService.Object,
            (trashRepository ?? MakeEmptyTrashRepository()).Object,
            (blockService ?? MakeEmptyBlockService()).Object,
            (favoriteRepository ?? MakeEmptyFavoriteRepository()).Object,
            (undoStack ?? new Mock<IUndoStack>()).Object);

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
