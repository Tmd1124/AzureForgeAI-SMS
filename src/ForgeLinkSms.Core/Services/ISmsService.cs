using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface ISmsService
{
    Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId);
    Task SendAsync(string address, string body);
}
