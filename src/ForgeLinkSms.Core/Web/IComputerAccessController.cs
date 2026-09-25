namespace ForgeLinkSms.Core.Web;

public interface IComputerAccessController
{
    bool IsRunning { get; }

    /// e.g. "http://192.168.1.220:8765" while running.
    string? Address { get; }

    /// False when the phone isn't on Wi-Fi.
    Task<bool> StartAsync();

    Task StopAsync();

    event Action? StateChanged;
}
