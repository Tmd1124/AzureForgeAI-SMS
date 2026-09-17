using Moq;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class SplashViewModelTests
{
    [Fact]
    public async Task InitializeAsync_navigates_to_conversations_when_already_default_app()
    {
        var roleService = new Mock<IDefaultAppRoleService>();
        roleService.Setup(r => r.IsDefaultSmsApp()).Returns(true);
        var nav = new Mock<INavigationService>();

        var viewModel = new SplashViewModel(roleService.Object, nav.Object);
        await viewModel.InitializeAsync();

        nav.Verify(n => n.NavigateToAsync("//conversations"), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_navigates_to_onboarding_when_not_default_app()
    {
        var roleService = new Mock<IDefaultAppRoleService>();
        roleService.Setup(r => r.IsDefaultSmsApp()).Returns(false);
        var nav = new Mock<INavigationService>();

        var viewModel = new SplashViewModel(roleService.Object, nav.Object);
        await viewModel.InitializeAsync();

        nav.Verify(n => n.NavigateToAsync("//onboarding"), Times.Once);
    }
}
