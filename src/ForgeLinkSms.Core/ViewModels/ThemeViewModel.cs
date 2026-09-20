using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

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
