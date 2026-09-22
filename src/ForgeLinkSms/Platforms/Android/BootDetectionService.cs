using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidSettings = global::Android.Provider.Settings;

namespace ForgeLinkSms.Platforms.Android;

public class BootDetectionService : IBootDetectionService
{
    private const string LastSeenBootCountKey = "last_seen_boot_count";

    // Settings.Global.BOOT_COUNT increments exactly once per device boot and is the documented,
    // reliable way to detect a reboot from inside an app — process death from the OS killing a
    // backgrounded app for memory (which also triggers a fresh cold start / splash otherwise)
    // does not touch it, so comparing it against the last value we recorded tells reboot apart
    // from an ordinary cold start.
    public bool IsFirstLaunchSinceBoot()
    {
        var currentBootCount = AndroidSettings.Global.GetInt(AndroidApp.Context.ContentResolver, AndroidSettings.Global.BootCount, -1);
        var lastSeenBootCount = Preferences.Get(LastSeenBootCountKey, -1);

        if (currentBootCount == lastSeenBootCount)
        {
            return false;
        }

        Preferences.Set(LastSeenBootCountKey, currentBootCount);
        return true;
    }
}
