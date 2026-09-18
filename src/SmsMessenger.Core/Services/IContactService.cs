using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Services;

public interface IContactService
{
    Task<ContactInfo?> LookupAsync(string phoneNumber);
    Task<IReadOnlyList<ContactInfo>> GetAllContactsAsync();
}
