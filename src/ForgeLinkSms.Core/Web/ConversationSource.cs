using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Web;

public interface IConversationSource
{
    Task<IReadOnlyList<SmsThread>> GetAsync(ConversationLane lane);

    /// Every conversation the phone's list shows in any lane (not trashed, archived, blocked, or snoozed).
    Task<IReadOnlyList<SmsThread>> GetAllAsync();
}

// Reuses the phone's own conversation-list logic so the browser sees exactly the same lists.
public class ConversationSource : IConversationSource
{
    private readonly Func<ConversationsViewModel> _create;

    public ConversationSource(Func<ConversationsViewModel> create)
    {
        _create = create;
    }

    public async Task<IReadOnlyList<SmsThread>> GetAsync(ConversationLane lane)
    {
        var viewModel = _create();
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Lane = lane;
        return viewModel.Threads.ToList();
    }

    public async Task<IReadOnlyList<SmsThread>> GetAllAsync()
    {
        var all = new List<SmsThread>();
        foreach (var lane in Enum.GetValues<ConversationLane>())
        {
            all.AddRange(await GetAsync(lane));
        }
        return all;
    }
}
