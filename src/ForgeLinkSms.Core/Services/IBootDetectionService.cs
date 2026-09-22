namespace ForgeLinkSms.Core.Services;

public interface IBootDetectionService
{
    /// True the first time this is called since the device was last rebooted; false on every
    /// subsequent call until the next reboot. Calling it records that this boot has been seen.
    bool IsFirstLaunchSinceBoot();
}
