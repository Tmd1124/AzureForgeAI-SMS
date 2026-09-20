using Moq;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class OnboardingViewModelTests
{
    [Fact]
    public void CanContinue_is_false_until_both_role_and_permissions_are_granted()
    {
        var role = new Mock<IDefaultAppRoleService>();
        var permissions = new Mock<IPermissionService>();
        var nav = new Mock<INavigationService>();
        var viewModel = new OnboardingViewModel(role.Object, permissions.Object, nav.Object);

        Assert.False(viewModel.CanContinue);
    }

    [Fact]
    public async Task RequestPermissionsCommand_sets_PermissionsGranted_on_success()
    {
        var role = new Mock<IDefaultAppRoleService>();
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.RequestAllAsync()).ReturnsAsync(true);
        var nav = new Mock<INavigationService>();
        var viewModel = new OnboardingViewModel(role.Object, permissions.Object, nav.Object);

        await viewModel.RequestPermissionsCommand.ExecuteAsync(null);

        Assert.True(viewModel.PermissionsGranted);
    }

    [Fact]
    public async Task ContinueCommand_navigates_to_conversations_when_both_granted()
    {
        var role = new Mock<IDefaultAppRoleService>();
        role.Setup(r => r.IsDefaultSmsApp()).Returns(true);
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.RequestAllAsync()).ReturnsAsync(true);
        var nav = new Mock<INavigationService>();
        var viewModel = new OnboardingViewModel(role.Object, permissions.Object, nav.Object);

        await viewModel.RequestRoleCommand.ExecuteAsync(null);
        await viewModel.RequestPermissionsCommand.ExecuteAsync(null);
        await viewModel.ContinueCommand.ExecuteAsync(null);

        nav.Verify(n => n.NavigateToAsync("/conversations"), Times.Once);
    }
}
