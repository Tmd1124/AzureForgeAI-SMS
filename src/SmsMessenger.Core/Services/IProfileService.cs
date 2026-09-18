using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Services;

public interface IProfileService
{
    UserProfile GetProfile();
    void SaveProfile(string displayName, string? photoPath);
    Task<string?> PickPhotoAsync();
}
