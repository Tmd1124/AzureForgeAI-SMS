using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public class SplashViewModel
{
    private readonly IDefaultAppRoleService _roleService;
    private readonly INavigationService _navigation;

    public SplashViewModel(IDefaultAppRoleService roleService, INavigationService navigation)
    {
        _roleService = roleService;
        _navigation = navigation;
    }

    public async Task InitializeAsync()
    {
        var route = _roleService.IsDefaultSmsApp() ? "/conversations" : "/onboarding";
        await _navigation.NavigateToAsync(route);
    }
}
