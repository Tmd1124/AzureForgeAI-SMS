using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IProfileService
{
    UserProfile GetProfile();
    void SaveProfile(string displayName, string? photoPath);
    Task<string?> PickPhotoAsync();
    Task<string?> GetProfilePhotoDataUriAsync();
}
