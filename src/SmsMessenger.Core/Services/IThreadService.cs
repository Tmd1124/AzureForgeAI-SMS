using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Services;

public interface IThreadService
{
    Task<IReadOnlyList<SmsThread>> GetThreadsAsync();
}
