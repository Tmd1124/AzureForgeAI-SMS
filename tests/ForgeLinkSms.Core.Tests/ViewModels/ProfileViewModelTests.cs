using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ProfileViewModelTests
{
    [Fact]
    public void Constructor_loads_the_currently_saved_profile()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "Travis", PhotoPath = "/path/photo.jpg" });

        var viewModel = new ProfileViewModel(profileService.Object);

        Assert.Equal("Travis", viewModel.DisplayName);
        Assert.Equal("/path/photo.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public async Task PickPhotoCommand_updates_PhotoPath_when_a_photo_is_picked()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = null });
        profileService.Setup(s => s.PickPhotoAsync()).ReturnsAsync("/path/new.jpg");
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal("/path/new.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public async Task PickPhotoCommand_leaves_PhotoPath_unchanged_when_cancelled()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = "/path/old.jpg" });
        profileService.Setup(s => s.PickPhotoAsync()).ReturnsAsync((string?)null);
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal("/path/old.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public void SaveCommand_saves_the_current_name_and_photo()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = null });
        var viewModel = new ProfileViewModel(profileService.Object) { DisplayName = "Travis", PhotoPath = "/path/photo.jpg" };

        viewModel.SaveCommand.Execute(null);

        profileService.Verify(s => s.SaveProfile("Travis", "/path/photo.jpg"), Times.Once);
    }
}
