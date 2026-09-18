using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDefaultAppRoleService _roleService;

    [ObservableProperty]
    private bool _isDefaultSmsApp;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    public SettingsViewModel(IDefaultAppRoleService roleService)
    {
        _roleService = roleService;
    }

    [RelayCommand]
    private Task Refresh()
    {
        IsDefaultSmsApp = _roleService.IsDefaultSmsApp();
        return Task.CompletedTask;
    }
}
