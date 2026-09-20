using Moq;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class MarkAsReadUndoActionTests
{
    [Fact]
    public async Task UndoAsync_marks_the_original_threads_unread_again()
    {
        var service = new Mock<IMarkAsReadService>();
        var action = new MarkAsReadUndoAction(new List<long> { 1, 2 }, service.Object);

        await action.UndoAsync();

        service.Verify(s => s.MarkThreadsAsUnreadAsync(It.Is<IReadOnlyList<long>>(ids => ids.SequenceEqual(new long[] { 1, 2 }))), Times.Once);
    }

    [Fact]
    public void Description_reports_the_thread_count()
    {
        var service = new Mock<IMarkAsReadService>();
        var action = new MarkAsReadUndoAction(new List<long> { 1, 2, 3 }, service.Object);

        Assert.Equal("Marked 3 conversation(s) as read", action.Description);
    }
}
