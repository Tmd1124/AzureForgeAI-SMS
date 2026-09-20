using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

public class ThemeService : IThemeService
{
    private const string ModeKey = "theme_mode";
    private const string AccentKey = "theme_accent";
    private const string DefaultAccent = "#25D366";

    public ThemeMode GetThemeMode()
    {
        var stored = Preferences.Get(ModeKey, nameof(ThemeMode.System));
        return Enum.TryParse<ThemeMode>(stored, out var mode) ? mode : ThemeMode.System;
    }

    public void SetThemeMode(ThemeMode mode) => Preferences.Set(ModeKey, mode.ToString());

    public string GetAccentColor() => Preferences.Get(AccentKey, DefaultAccent);

    public void SetAccentColor(string hexColor) => Preferences.Set(AccentKey, hexColor);
}
