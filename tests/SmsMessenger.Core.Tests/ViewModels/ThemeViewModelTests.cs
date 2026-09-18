using Moq;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ThemeViewModelTests
{
    [Fact]
    public void Constructor_loads_the_currently_saved_mode_and_accent()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(s => s.GetThemeMode()).Returns(ThemeMode.Dark);
        themeService.Setup(s => s.GetAccentColor()).Returns("#ff0000");

        var viewModel = new ThemeViewModel(themeService.Object);

        Assert.Equal(ThemeMode.Dark, viewModel.SelectedMode);
        Assert.Equal("#ff0000", viewModel.AccentColor);
    }

    [Fact]
    public void SelectModeCommand_updates_the_property_and_saves_it()
    {
        var themeService = new Mock<IThemeService>();
        var viewModel = new ThemeViewModel(themeService.Object);

        viewModel.SelectModeCommand.Execute(ThemeMode.Light);

        Assert.Equal(ThemeMode.Light, viewModel.SelectedMode);
        themeService.Verify(s => s.SetThemeMode(ThemeMode.Light), Times.Once);
    }

    [Fact]
    public void SelectAccentCommand_updates_the_property_and_saves_it()
    {
        var themeService = new Mock<IThemeService>();
        var viewModel = new ThemeViewModel(themeService.Object);

        viewModel.SelectAccentCommand.Execute("#00ff00");

        Assert.Equal("#00ff00", viewModel.AccentColor);
        themeService.Verify(s => s.SetAccentColor("#00ff00"), Times.Once);
    }
}
