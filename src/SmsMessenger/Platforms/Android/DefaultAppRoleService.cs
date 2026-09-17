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

        // Telephony.Sms.GetDefaultSmsPackage(Context) was observed to
        // return an empty string on this device/OS version (Android 15,
        // One UI) even immediately after the app was confirmed as the
        // android.app.role.SMS holder via `cmd role get-role-holders`.
        // RoleManager is the source of truth on Q+; only fall back to the
        // legacy API pre-Q where RoleManager doesn't exist.
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var roleManager = (RoleManager)context.GetSystemService(Context.RoleService)!;
            return roleManager.IsRoleHeld(RoleManager.RoleSms);
        }

        var currentDefault = Telephony.Sms.GetDefaultSmsPackage(context);
        return string.Equals(myPackage, currentDefault, StringComparison.Ordinal);
    }

    private const int RequestRoleCode = 1001;

    public Task<bool> RequestDefaultSmsAppAsync()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var context = (Context?)activity ?? AndroidApp.Context;
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

        // RequestRoleActivity identifies the requesting app via
        // Activity.getCallingPackage(), which Android only populates when
        // the intent is launched with startActivityForResult (not a plain
        // startActivity/FLAG_ACTIVITY_NEW_TASK call, which was observed to
        // make RequestRoleActivity log "Package name cannot be null or
        // empty: null" and immediately self-finish with no UI). We don't
        // need the actual result here (see comment below) but the call
        // must go through the for-result path for the OS to show the
        // dialog at all.
        if (activity is not null)
        {
            activity.StartActivityForResult(intent, RequestRoleCode);
        }
        else
        {
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }

        // The system role-request dialog is asynchronous and its result
        // arrives on MainActivity's OnActivityResult, not here. Callers
        // re-check IsDefaultSmsApp() when the app resumes (see
        // OnboardingViewModel in Task 6) rather than awaiting a result
        // from this call directly.
        return Task.FromResult(false);
    }
}
