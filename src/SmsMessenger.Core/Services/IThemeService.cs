using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Services;

public interface IThemeService
{
    ThemeMode GetThemeMode();
    void SetThemeMode(ThemeMode mode);
    string GetAccentColor();
    void SetAccentColor(string hexColor);
}
