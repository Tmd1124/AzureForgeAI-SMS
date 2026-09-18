using Moq;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ConversationsViewModelTests
{
    private static SmsThread MakeThread(long id, string address, string? name, string lastMessage) => new()
    {
        Id = id,
        Address = address,
        DisplayName = name,
        LastMessageBody = lastMessage,
        LastMessageTimestamp = DateTimeOffset.UtcNow,
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

    [Fact]
    public async Task LoadCommand_populates_Threads_from_the_service()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);

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
        var viewModel = new ConversationsViewModel(threadService.Object, trashRepository.Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);

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
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, blockService.Object, new Mock<IUndoStack>().Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Threads);
        Assert.Equal(1, viewModel.Threads[0].Id);
    }

    [Fact]
    public async Task TrashThreadCommand_trashes_the_thread_pushes_an_undo_action_and_removes_it_from_Threads()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi")
        });
        var trashRepository = MakeEmptyTrashRepository();
        var undoStack = new Mock<IUndoStack>();
        var viewModel = new ConversationsViewModel(threadService.Object, trashRepository.Object, MakeEmptyBlockService().Object, undoStack.Object);
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
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, blockService.Object, undoStack.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.BlockThreadCommand.ExecuteAsync("5550142231");

        blockService.Verify(s => s.BlockAsync("5550142231"), Times.Once);
        undoStack.Verify(s => s.Push(It.IsAny<IUndoableAction>()), Times.Once);
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
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);
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
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);
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
        var viewModel = new ConversationsViewModel(threadService.Object, MakeEmptyTrashRepository().Object, MakeEmptyBlockService().Object, new Mock<IUndoStack>().Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "gym";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }
}
