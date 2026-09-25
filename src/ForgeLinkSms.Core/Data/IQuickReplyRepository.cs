using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public interface IQuickReplyRepository
{
    Task InitializeAsync();
    Task<IReadOnlyList<QuickReply>> GetAllAsync();
    Task AddAsync(string text);
    Task UpdateAsync(long id, string text);
    Task DeleteAsync(long id);
}
