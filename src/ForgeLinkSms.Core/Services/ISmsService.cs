using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface ISmsService
{
    /// Returns up to pageSize messages for the thread, newest first, optionally only those
    /// strictly before beforeTimestamp (for fetching the next page of older history).
    Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId, DateTimeOffset? beforeTimestamp, int pageSize);

    /// Returns up to pageSize messages for the thread, oldest first, strictly after
    /// afterTimestamp — the downward counterpart to GetMessagesAsync, used to catch a trimmed
    /// loaded window back up to the thread's true latest message.
    Task<IReadOnlyList<SmsMessage>> GetNewerMessagesAsync(long threadId, DateTimeOffset afterTimestamp, int pageSize);

    Task SendAsync(string address, string body);

    Task SendMmsAsync(long threadId, string address, string? body, PickedAttachment attachment);
}
