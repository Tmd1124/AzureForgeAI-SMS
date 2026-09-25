namespace ForgeLinkSms.Core.Data;

public interface IAllowedSenderRepository
{
    Task InitializeAsync();
    Task AllowAsync(string normalizedAddress);
    Task DisallowAsync(string normalizedAddress);
    Task<bool> IsAllowedAsync(string normalizedAddress);
    Task<IReadOnlySet<string>> GetAllAsync();
}
