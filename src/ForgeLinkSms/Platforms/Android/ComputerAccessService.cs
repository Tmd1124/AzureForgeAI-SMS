using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Platforms.Android;

// Keeps the Computer access server alive in the background. Android requires a visible
// notification for this, which doubles as the user's reminder that sharing is on.
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public class ComputerAccessService : Service
{
    public const string StopAction = "forgelink.computer_access.STOP";
    private const string ChannelId = "computer_access";
    private const int NotificationId = 8765;

    private static readonly ComputerAccessServer Server = new();
    private WifiManager.WifiLock? _wifiLock;

    public static bool IsRunning { get; private set; }
    public static event Action? StateChanged;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction)
        {
            StopSharing();
            return StartCommandResult.NotSticky;
        }

        StartForegroundWithNotification();
        var services = MauiApplication.Current.Services;
        try
        {
            Server.Start(services.GetRequiredService<WebApi>(), services.GetRequiredService<WebEventHub>(), services.GetRequiredService<PairingService>());
        }
        catch (System.Net.HttpListenerException ex)
        {
            global::Android.Util.Log.Warn("ForgeLinkSms", $"Computer access couldn't start: {ex.Message}");
            global::Android.Widget.Toast.MakeText(this, $"Couldn't turn on computer access (port {ComputerAccessServer.Port} is busy). Try again in a minute.", global::Android.Widget.ToastLength.Long)?.Show();
            StopSharing();
            return StartCommandResult.NotSticky;
        }

        // Without this, Wi-Fi may doze while the screen is off and the computer loses the phone.
        var wifi = (WifiManager?)GetSystemService(WifiService);
#pragma warning disable CA1422 // still the only way to keep Wi-Fi fully awake for a local server
        _wifiLock = wifi?.CreateWifiLock(WifiMode.FullHighPerf, "ForgeLink computer access");
#pragma warning restore CA1422
        _wifiLock?.Acquire();

        IsRunning = true;
        StateChanged?.Invoke();
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopSharing();
        base.OnDestroy();
    }

    private void StopSharing()
    {
        Server.Stop();
        if (_wifiLock?.IsHeld == true)
        {
            _wifiLock.Release();
        }
        _wifiLock = null;
        IsRunning = false;
        StateChanged?.Invoke();
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    private void StartForegroundWithNotification()
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && manager.GetNotificationChannel(ChannelId) is null)
        {
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Computer access", NotificationImportance.Low));
        }

        var stopIntent = new Intent(this, typeof(ComputerAccessService)).SetAction(StopAction);
        var stopPending = PendingIntent.GetService(this, 0, stopIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var openIntent = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        openIntent.PutExtra("initial_route", "/computer-access");
        var openPending = PendingIntent.GetActivity(this, 1, openIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("ForgeLink is sharing messages with your computer")
            .SetContentText(ComputerAccessController.CurrentAddress() ?? "Computer access is on")
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysUpload)
            .SetOngoing(true)
            .SetContentIntent(openPending)
            .AddAction(0, "Turn off", stopPending)
            .Build();

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }
    }
}

public class ComputerAccessController : IComputerAccessController
{
    public ComputerAccessController()
    {
        ComputerAccessService.StateChanged += () => StateChanged?.Invoke();
    }

    public bool IsRunning => ComputerAccessService.IsRunning;

    public string? Address => IsRunning ? CurrentAddress() : null;

    public event Action? StateChanged;

    public Task<bool> StartAsync()
    {
        if (CurrentAddress() is null)
        {
            return Task.FromResult(false);
        }
        var context = Platform.AppContext;
        ContextCompat.StartForegroundService(context, new Intent(context, typeof(ComputerAccessService)));
        return Task.FromResult(true);
    }

    public Task StopAsync()
    {
        var context = Platform.AppContext;
        context.StartService(new Intent(context, typeof(ComputerAccessService)).SetAction(ComputerAccessService.StopAction));
        return Task.CompletedTask;
    }

    // The phone's IPv4 address on its Wi-Fi network, or null when not on Wi-Fi.
    public static string? CurrentAddress()
    {
        var connectivity = (ConnectivityManager?)Platform.AppContext.GetSystemService(Context.ConnectivityService);
        var network = connectivity?.ActiveNetwork;
        var capabilities = network is null ? null : connectivity!.GetNetworkCapabilities(network);
        if (capabilities is null || !capabilities.HasTransport(TransportType.Wifi))
        {
            return null;
        }
        var ipv4 = connectivity!.GetLinkProperties(network)?.LinkAddresses
            .Select(l => l.Address)
            .FirstOrDefault(a => a is Java.Net.Inet4Address);
        return ipv4 is null ? null : $"http://{ipv4.HostAddress}:{ComputerAccessServer.Port}";
    }
}
