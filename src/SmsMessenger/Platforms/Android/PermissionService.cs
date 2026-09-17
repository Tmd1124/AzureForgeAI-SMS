using Microsoft.Maui.ApplicationModel;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Platforms.Android;

public class PermissionService : IPermissionService
{
    private static readonly (Func<Task<PermissionStatus>> CheckAndRequest, string Name)[] PermissionRequests =
    {
        (() => Permissions.RequestAsync<Permissions.Sms>(), "SMS"),
        (() => Permissions.RequestAsync<Permissions.ContactsRead>(), "Contacts"),
        (() => Permissions.RequestAsync<Permissions.Phone>(), "Phone State"),
        (() => Permissions.RequestAsync<Permissions.PostNotifications>(), "Notifications"),
    };

    public async Task<bool> RequestAllAsync()
    {
        var allGranted = true;
        foreach (var (request, _) in PermissionRequests)
        {
            var status = await request();
            if (status != PermissionStatus.Granted)
            {
                allGranted = false;
            }
        }
        return allGranted;
    }
}
