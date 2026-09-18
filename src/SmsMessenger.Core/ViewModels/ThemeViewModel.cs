using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class ThemeViewModel : ObservableObject
{
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private ThemeMode _selectedMode;

    [ObservableProperty]
    private string _accentColor = string.Empty;

    public ThemeViewModel(IThemeService themeService)
    {
        _themeService = themeService;
        _selectedMode = _themeService.GetThemeMode();
        _accentColor = _themeService.GetAccentColor();
    }

    [RelayCommand]
    private void SelectMode(ThemeMode mode)
    {
        SelectedMode = mode;
        _themeService.SetThemeMode(mode);
    }

    [RelayCommand]
    private void SelectAccent(string hexColor)
    {
        AccentColor = hexColor;
        _themeService.SetAccentColor(hexColor);
    }
}
