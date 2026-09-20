namespace ForgeLinkSms.Core.Services;

public interface IDefaultAppRoleService
{
    bool IsDefaultSmsApp();
    Task<bool> RequestDefaultSmsAppAsync();
}
