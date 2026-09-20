using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Platforms.Android;

public class ProfileService : IProfileService
{
    private const string NameKey = "profile_display_name";
    private const string PhotoKey = "profile_photo_path";

    public UserProfile GetProfile()
    {
        var photoPath = Preferences.Get(PhotoKey, string.Empty);
        return new UserProfile
        {
            DisplayName = Preferences.Get(NameKey, string.Empty),
            PhotoPath = string.IsNullOrEmpty(photoPath) ? null : photoPath
        };
    }

    public void SaveProfile(string displayName, string? photoPath)
    {
        Preferences.Set(NameKey, displayName);
        if (photoPath is not null)
        {
            Preferences.Set(PhotoKey, photoPath);
        }
    }

    public async Task<string?> PickPhotoAsync()
    {
        var result = await MediaPicker.Default.PickPhotoAsync();
        if (result is null)
        {
            return null;
        }

        var destinationPath = Path.Combine(FileSystem.AppDataDirectory, "profile_photo" + Path.GetExtension(result.FileName));
        using var sourceStream = await result.OpenReadAsync();
        using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream);

        return destinationPath;
    }

    public Task<string?> GetProfilePhotoDataUriAsync() => ImageDataUriHelper.ToDataUriAsync(GetProfile().PhotoPath);
}
