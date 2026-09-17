using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    private readonly IDefaultAppRoleService _roleService;
    private readonly IPermissionService _permissionService;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private bool _roleGranted;

    [ObservableProperty]
    private bool _permissionsGranted;

    public bool CanContinue => RoleGranted && PermissionsGranted;

    public OnboardingViewModel(IDefaultAppRoleService roleService, IPermissionService permissionService, INavigationService navigation)
    {
        _roleService = roleService;
        _permissionService = permissionService;
        _navigation = navigation;
    }

    [RelayCommand]
    private async Task RequestRole()
    {
        await _roleService.RequestDefaultSmsAppAsync();
        RoleGranted = _roleService.IsDefaultSmsApp();
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand]
    private async Task RequestPermissions()
    {
        PermissionsGranted = await _permissionService.RequestAllAsync();
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand]
    private async Task Continue()
    {
        if (CanContinue)
        {
            await _navigation.NavigateToAsync("/conversations");
        }
    }
}
