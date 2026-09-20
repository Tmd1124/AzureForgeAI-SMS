using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IThreadService
{
    Task<IReadOnlyList<SmsThread>> GetThreadsAsync();
}
