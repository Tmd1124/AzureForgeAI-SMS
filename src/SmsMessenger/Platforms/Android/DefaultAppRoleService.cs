using Android.App.Roles;
using Android.Content;
using Android.OS;
using Android.Provider;
using SmsMessenger.Core.Services;
using AndroidApp = Android.App.Application;

namespace SmsMessenger.Platforms.Android;

public class DefaultAppRoleService : IDefaultAppRoleService
{
    public bool IsDefaultSmsApp()
    {
        var context = AndroidApp.Context;
        var myPackage = context.PackageName;
        var currentDefault = Telephony.Sms.GetDefaultSmsPackage(context);
        return string.Equals(myPackage, currentDefault, StringComparison.Ordinal);
    }

    public Task<bool> RequestDefaultSmsAppAsync()
    {
        var context = AndroidApp.Context;
        Intent intent;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var roleManager = (RoleManager)context.GetSystemService(Context.RoleService)!;
            intent = roleManager.CreateRequestRoleIntent(RoleManager.RoleSms);
        }
        else
        {
            intent = new Intent(Telephony.Sms.Intents.ActionChangeDefault);
            intent.PutExtra(Telephony.Sms.Intents.ExtraPackageName, context.PackageName);
        }

        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);

        // The system role-request dialog is asynchronous and its result
        // arrives on MainActivity's OnActivityResult, not here. Callers
        // re-check IsDefaultSmsApp() when the app resumes (see
        // OnboardingViewModel in Task 6) rather than awaiting a result
        // from this call directly.
        return Task.FromResult(false);
    }
}
