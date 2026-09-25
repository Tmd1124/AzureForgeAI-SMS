using System.Net;
using System.Text;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Platforms.Android;

// Socket side of Computer access: turns HttpListener requests into WebApi calls and keeps the
// /api/events stream open for live updates.
public sealed class ComputerAccessServer
{
    public const int Port = 8765;
    private const int MaxBodyBytes = 64 * 1024;
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public void Start(WebApi api, WebEventHub events, PairingService pairing)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{Port}/");
        _listener.Start();
        var listener = _listener;
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception) when (token.IsCancellationRequested || !listener.IsListening)
                {
                    return;
                }
                _ = Task.Run(() => HandleAsync(context, api, events, pairing, token));
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        _listener = null;
    }

    private static async Task HandleAsync(HttpListenerContext context, WebApi api, WebEventHub events, PairingService pairing, CancellationToken stopping)
    {
        var response = context.Response;
        try
        {
            var request = context.Request;
            var client = request.RemoteEndPoint?.Address.ToString() ?? string.Empty;
            var query = request.QueryString.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => request.QueryString[k] ?? string.Empty);
            var authorization = request.Headers["Authorization"];
            var token = authorization?.StartsWith("Bearer ", StringComparison.Ordinal) == true
                ? authorization["Bearer ".Length..]
                : query.GetValueOrDefault("token");
            var path = request.Url?.AbsolutePath ?? "/";

            if (path == "/api/events")
            {
                await StreamEventsAsync(response, client, token, events, pairing, stopping);
                return;
            }

            if (!PrivateNetwork.IsPrivate(client))
            {
                response.StatusCode = 403;
                return;
            }

            string? body = null;
            if (request.HasEntityBody)
            {
                body = request.ContentLength64 > MaxBodyBytes ? null : await RequestBody.ReadAsync(request.InputStream, MaxBodyBytes, stopping);
                if (body is null)
                {
                    response.StatusCode = 413;
                    return;
                }
            }

            var result = await api.HandleAsync(new ForgeLinkSms.Core.Web.WebRequest(request.HttpMethod, path, query, body, token, client));
            response.StatusCode = result.Status;
            response.ContentType = result.ContentType;
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = result.Body.Length;
            await response.OutputStream.WriteAsync(result.Body, stopping);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("ForgeLinkSms", $"Computer access request failed: {ex.Message}");
            try
            {
                response.StatusCode = 500;
            }
            catch (InvalidOperationException)
            {
            }
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception)
            {
            }
        }
    }

    // Server-Sent Events: EventSource can't send headers, so the token comes in the query string.
    private static async Task StreamEventsAsync(HttpListenerResponse response, string client, string? token, WebEventHub events, PairingService pairing, CancellationToken stopping)
    {
        if (!PrivateNetwork.IsPrivate(client) || !await pairing.IsAuthorizedAsync(token))
        {
            response.StatusCode = 401;
            return;
        }

        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-store";
        response.SendChunked = true;
        var (id, reader) = events.Subscribe();
        try
        {
            var output = response.OutputStream;
            await WriteAsync(output, ": connected\n\n", stopping);
            while (!stopping.IsCancellationRequested)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                wait.CancelAfter(Heartbeat);
                string message;
                try
                {
                    message = $"data: {await reader.ReadAsync(wait.Token)}\n\n";
                }
                catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
                {
                    message = ": ping\n\n";
                }
                await WriteAsync(output, message, stopping);
            }
        }
        catch (Exception)
        {
            // The browser went away (closed tab, sleep); it reconnects on its own.
        }
        finally
        {
            events.Unsubscribe(id);
        }
    }

    private static async Task WriteAsync(Stream output, string text, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await output.WriteAsync(bytes, token);
        await output.FlushAsync(token);
    }
}
