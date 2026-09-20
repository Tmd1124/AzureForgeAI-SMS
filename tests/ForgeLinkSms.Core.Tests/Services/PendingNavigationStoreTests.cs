using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class PendingNavigationStoreTests
{
    [Fact]
    public void ConsumePendingRoute_returns_null_when_nothing_pending()
    {
        var store = new PendingNavigationStore();

        Assert.Null(store.ConsumePendingRoute());
    }

    [Fact]
    public void ConsumePendingRoute_returns_the_route_that_was_set()
    {
        var store = new PendingNavigationStore();

        store.SetPendingRoute("/conversations/thread?id=1&address=5550142231");

        Assert.Equal("/conversations/thread?id=1&address=5550142231", store.ConsumePendingRoute());
    }

    [Fact]
    public void ConsumePendingRoute_clears_the_route_so_it_is_only_consumed_once()
    {
        var store = new PendingNavigationStore();
        store.SetPendingRoute("/conversations/thread?id=1&address=5550142231");

        store.ConsumePendingRoute();

        Assert.Null(store.ConsumePendingRoute());
    }
}
