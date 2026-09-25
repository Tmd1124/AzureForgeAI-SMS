using System.Text.Json;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.Tests.Web;

public class WebEventHubTests
{
    private readonly IncomingMessageNotifier _notifier = new();

    [Fact]
    public async Task A_subscriber_hears_about_new_messages()
    {
        using var hub = new WebEventHub(_notifier);
        var (_, events) = hub.Subscribe();

        _notifier.NotifyMessageReceived(7);

        var json = await events.ReadAsync();
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("message", root.GetProperty("type").GetString());
        Assert.Equal(7, root.GetProperty("threadId").GetInt64());
    }

    [Fact]
    public void An_unsubscribed_browser_gets_nothing()
    {
        using var hub = new WebEventHub(_notifier);
        var (id, events) = hub.Subscribe();

        hub.Unsubscribe(id);
        _notifier.NotifyMessageReceived(7);

        Assert.False(events.TryRead(out _));
        Assert.Equal(0, hub.SubscriberCount);
    }

    [Fact]
    public void A_browser_that_stops_reading_never_blocks_the_phone()
    {
        using var hub = new WebEventHub(_notifier);
        hub.Subscribe();

        for (var i = 0; i < 1000; i++)
        {
            _notifier.NotifyMessageReceived(i);
        }

        Assert.Equal(1, hub.SubscriberCount);
    }
}
