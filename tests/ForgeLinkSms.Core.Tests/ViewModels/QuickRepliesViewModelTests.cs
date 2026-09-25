using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class QuickRepliesViewModelTests
{
    private static Mock<IQuickReplyRepository> RepositoryWith(params string[] texts)
    {
        var repository = new Mock<IQuickReplyRepository>();
        repository.Setup(r => r.GetAllAsync()).ReturnsAsync(texts.Select((t, i) => new QuickReply { Id = i + 1, Text = t, SortOrder = i }).ToList());
        return repository;
    }

    [Fact]
    public async Task LoadCommand_lists_the_saved_replies()
    {
        var viewModel = new QuickRepliesViewModel(RepositoryWith("On my way!", "Sounds good 👍").Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "On my way!", "Sounds good 👍" }, viewModel.Replies.Select(r => r.Text));
    }

    [Fact]
    public async Task AddCommand_saves_trimmed_text_and_reloads()
    {
        var repository = RepositoryWith();
        var viewModel = new QuickRepliesViewModel(repository.Object);

        await viewModel.AddCommand.ExecuteAsync("  Call me later  ");

        repository.Verify(r => r.AddAsync("Call me later"), Times.Once);
        repository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddCommand_ignores_blank_text(string text)
    {
        var repository = RepositoryWith();
        var viewModel = new QuickRepliesViewModel(repository.Object);

        await viewModel.AddCommand.ExecuteAsync(text);

        repository.Verify(r => r.AddAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCommand_saves_the_new_text()
    {
        var repository = RepositoryWith("On my way!");
        var viewModel = new QuickRepliesViewModel(repository.Object);

        await viewModel.UpdateCommand.ExecuteAsync((1, "On my way, 10 min"));

        repository.Verify(r => r.UpdateAsync(1, "On my way, 10 min"), Times.Once);
    }

    [Fact]
    public async Task UpdateCommand_with_blank_text_deletes_the_reply()
    {
        var repository = RepositoryWith("On my way!");
        var viewModel = new QuickRepliesViewModel(repository.Object);

        await viewModel.UpdateCommand.ExecuteAsync((1, "  "));

        repository.Verify(r => r.DeleteAsync(1), Times.Once);
        repository.Verify(r => r.UpdateAsync(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCommand_removes_the_reply()
    {
        var repository = RepositoryWith("On my way!");
        var viewModel = new QuickRepliesViewModel(repository.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteCommand.ExecuteAsync(1L);

        repository.Verify(r => r.DeleteAsync(1), Times.Once);
    }
}
