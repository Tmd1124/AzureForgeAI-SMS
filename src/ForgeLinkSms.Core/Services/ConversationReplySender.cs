namespace ForgeLinkSms.Core.Services;

// Sends a reply typed outside the conversation screen (e.g. in a notification), picking a plain
// text or a group message the same way the conversation screen does.
public class ConversationReplySender
{
    private readonly ISmsService _smsService;
    private readonly IThreadService _threadService;
    private readonly IMarkAsReadService _markAsReadService;

    public ConversationReplySender(ISmsService smsService, IThreadService threadService, IMarkAsReadService markAsReadService)
    {
        _smsService = smsService;
        _threadService = threadService;
        _markAsReadService = markAsReadService;
    }

    public async Task SendAsync(long threadId, string address, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var participants = await _threadService.GetParticipantsAsync(threadId);
        if (participants.Count > 1)
        {
            await _smsService.SendGroupAsync(threadId, participants, trimmed, null);
        }
        else
        {
            await _smsService.SendAsync(address, trimmed);
        }

        // Replying means the user has seen the conversation.
        await _markAsReadService.MarkThreadAsReadAsync(threadId);
    }
}
