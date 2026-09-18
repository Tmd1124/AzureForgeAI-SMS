using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Data;

public interface IBlockedNumberRepository
{
    Task InitializeAsync();
    Task BlockAsync(string phoneNumber);
    Task UnblockAsync(string phoneNumber);
    Task<bool> IsBlockedAsync(string phoneNumber);
    Task<IReadOnlyList<BlockedNumber>> GetBlockedNumbersAsync();
}
