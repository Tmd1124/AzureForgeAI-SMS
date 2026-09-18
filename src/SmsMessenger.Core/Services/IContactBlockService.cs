namespace SmsMessenger.Core.Services;

public interface IContactBlockService
{
    Task BlockAsync(string phoneNumber);
    Task UnblockAsync(string phoneNumber);
    Task<bool> IsBlockedAsync(string phoneNumber);
    Task<IReadOnlyList<string>> GetBlockedNumbersAsync();
}
