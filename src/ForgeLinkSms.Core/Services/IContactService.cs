using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IContactService
{
    Task<ContactInfo?> LookupAsync(string phoneNumber);
    Task<IReadOnlyList<ContactInfo>> GetAllContactsAsync();
    Task AddContactAsync(string phoneNumber);
    Task OpenContactAsync(string phoneNumber);
}
