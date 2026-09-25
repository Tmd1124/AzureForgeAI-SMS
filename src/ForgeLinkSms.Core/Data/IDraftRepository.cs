namespace ForgeLinkSms.Core.Data;

public interface IDraftRepository
{
    Task InitializeAsync();
    Task<string?> GetAsync(long threadId);

    /// Blank text removes the draft.
    Task SaveAsync(long threadId, string text);

    Task<IReadOnlyDictionary<long, string>> GetAllAsync();
}
