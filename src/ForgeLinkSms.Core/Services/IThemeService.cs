using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IThemeService
{
    ThemeMode GetThemeMode();
    void SetThemeMode(ThemeMode mode);
    string GetAccentColor();
    void SetAccentColor(string hexColor);
}
