namespace SmsMessenger.Core.Services;

public interface IPermissionService
{
    Task<bool> RequestAllAsync();
}
