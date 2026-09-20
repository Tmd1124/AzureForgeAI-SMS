namespace ForgeLinkSms.Core.Services;

/// Bridges native Android entry points (notification taps, ACTION_SENDTO hand-offs) that launch
/// MainActivity with a target route into the Blazor circuit, which has no other way to receive it —
/// MainActivity and the BlazorWebView's NavigationManager live in different DI scopes.
public class PendingNavigationStore
{
    private readonly object _lock = new();
    private string? _pendingRoute;

    public void SetPendingRoute(string route)
    {
        lock (_lock)
        {
            _pendingRoute = route;
        }
    }

    public string? ConsumePendingRoute()
    {
        lock (_lock)
        {
            var route = _pendingRoute;
            _pendingRoute = null;
            return route;
        }
    }
}
