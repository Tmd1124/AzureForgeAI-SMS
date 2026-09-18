using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly IProfileService _profileService;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _photoPath;

    public ProfileViewModel(IProfileService profileService)
    {
        _profileService = profileService;
        var profile = _profileService.GetProfile();
        _displayName = profile.DisplayName;
        _photoPath = profile.PhotoPath;
    }

    [RelayCommand]
    private async Task PickPhoto()
    {
        var path = await _profileService.PickPhotoAsync();
        if (path is not null)
        {
            PhotoPath = path;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _profileService.SaveProfile(DisplayName, PhotoPath);
    }
}
