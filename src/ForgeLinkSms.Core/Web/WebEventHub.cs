using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Web;

// Fans phone events out to every connected browser's event stream.
public sealed class WebEventHub : IDisposable
{
    private const int BufferPerBrowser = 100;

    private readonly IIncomingMessageNotifier _notifier;
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();

    public WebEventHub(IIncomingMessageNotifier notifier)
    {
        _notifier = notifier;
        _notifier.MessageReceived += OnMessageReceived;
    }

    public int SubscriberCount => _subscribers.Count;

    public (Guid Id, ChannelReader<string> Events) Subscribe()
    {
        // DropOldest: a stalled browser loses old events instead of ever blocking the phone.
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(BufferPerBrowser) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public void Publish(string json)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(json);
        }
    }

    private void OnMessageReceived(long threadId) =>
        Publish(JsonSerializer.Serialize(new { type = "message", threadId }, WebApi.JsonOptions));

    public void Dispose()
    {
        _notifier.MessageReceived -= OnMessageReceived;
        foreach (var id in _subscribers.Keys)
        {
            Unsubscribe(id);
        }
    }
}
