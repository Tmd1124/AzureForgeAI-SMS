namespace ForgeLinkSms.Core.Services;

public interface IPermissionService
{
    Task<bool> RequestAllAsync();
}
