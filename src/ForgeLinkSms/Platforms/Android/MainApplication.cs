using Android.App;
using Android.Runtime;

[assembly: UsesPermission(Android.Manifest.Permission.ReadSms)]
[assembly: UsesPermission(Android.Manifest.Permission.SendSms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveSms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveMms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadContacts)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadPhoneState)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveWapPush)]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission(Android.Manifest.Permission.AccessFineLocation)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessCoarseLocation)]

namespace ForgeLinkSms;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
