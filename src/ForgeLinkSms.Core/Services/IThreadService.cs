using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IThreadService
{
    Task<IReadOnlyList<SmsThread>> GetThreadsAsync();

    /// Everyone in the conversation other than the user: one address for a normal conversation,
    /// several for a group.
    Task<IReadOnlyList<string>> GetParticipantsAsync(long threadId);
}
