using System.Text.Json;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Web;

// The browser's JSON API. Transport-free (plain request/response records) so it's testable here
// and can be hosted by the phone today or a cloud relay later.
public class WebApi
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int PageSize = 50;
    private const int MaxSearchResults = 50;

    private readonly PairingService _pairing;
    private readonly IConversationSource _conversations;
    private readonly ISmsService _sms;
    private readonly IMarkAsReadService _markAsRead;
    private readonly IMediaThumbnailService _thumbnails;
    private readonly IContactService _contacts;
    private readonly IStaticFiles _staticFiles;
    private readonly ConversationReplySender _replySender;

    public WebApi(PairingService pairing, IConversationSource conversations, ISmsService sms, IThreadService threads,
        IMarkAsReadService markAsRead, IMediaThumbnailService thumbnails, IContactService contacts, IStaticFiles staticFiles)
    {
        _pairing = pairing;
        _conversations = conversations;
        _sms = sms;
        _markAsRead = markAsRead;
        _thumbnails = thumbnails;
        _contacts = contacts;
        _staticFiles = staticFiles;
        _replySender = new ConversationReplySender(sms, threads, markAsRead);
    }

    private sealed record PairBody(string? Code);
    private sealed record ReplyBody(string? Address, string? Text);
    private sealed record NewMessageBody(string[]? To, string? Text);

    public async Task<WebResponse> HandleAsync(WebRequest request)
    {
        if (!PrivateNetwork.IsPrivate(request.ClientAddress))
        {
            return WebResponse.Error(403, "not-home-network");
        }

        if (!request.Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            return await StaticAsync(request.Path);
        }

        if (request.Method == "POST" && request.Path == "/api/pair")
        {
            return await PairAsync(request);
        }

        if (!await _pairing.IsAuthorizedAsync(request.Token))
        {
            return WebResponse.Error(401, "unpaired");
        }

        var segments = request.Path.Trim('/').Split('/');
        try
        {
            return (request.Method, segments) switch
            {
                ("GET", ["api", "conversations"]) => await ConversationsAsync(request),
                ("GET", ["api", "conversations", var id, "messages"]) when long.TryParse(id, out var threadId) => await MessagesAsync(threadId, request),
                ("POST", ["api", "conversations", var id, "read"]) when long.TryParse(id, out var threadId) => await MarkReadAsync(threadId),
                ("POST", ["api", "conversations", var id, "reply"]) when long.TryParse(id, out var threadId) => await ReplyAsync(threadId, request),
                ("POST", ["api", "messages", "new"]) => await NewMessageAsync(request),
                ("GET", ["api", "search"]) => await SearchAsync(request),
                ("GET", ["api", "contacts"]) => await ContactsAsync(request),
                ("GET", ["api", "media", var id]) when long.TryParse(id, out var partId) => await MediaAsync(partId, request),
                _ => WebResponse.Error(404, "not-found")
            };
        }
        catch (JsonException)
        {
            return WebResponse.Error(400, "bad-json");
        }
    }

    private async Task<WebResponse> StaticAsync(string path)
    {
        var file = path is "/" or "" ? "index.html" : path.TrimStart('/');
        if (file.Contains("..", StringComparison.Ordinal))
        {
            return WebResponse.Error(404, "not-found");
        }
        return await _staticFiles.GetAsync(file) is { } found
            ? new WebResponse(200, found.ContentType, found.Content)
            : WebResponse.Error(404, "not-found");
    }

    private async Task<WebResponse> PairAsync(WebRequest request)
    {
        var body = JsonSerializer.Deserialize<PairBody>(request.Body ?? "{}", JsonOptions);
        var result = await _pairing.PairAsync(body?.Code ?? string.Empty, request.ClientAddress);
        return result.Outcome switch
        {
            PairingOutcome.Paired => WebResponse.Json(new { token = result.Token }),
            PairingOutcome.LockedOut => WebResponse.Error(429, "locked"),
            _ => WebResponse.Error(401, "wrong-code")
        };
    }

    private async Task<WebResponse> ConversationsAsync(WebRequest request)
    {
        var lane = request.Query.GetValueOrDefault("lane") switch
        {
            "updates" => ConversationLane.Updates,
            "screener" => ConversationLane.Screener,
            _ => ConversationLane.Conversations
        };
        var threads = await _conversations.GetAsync(lane);
        return WebResponse.Json(threads.Select(t => new
        {
            id = t.Id,
            name = t.DisplayNameOrAddress,
            address = t.Address,
            preview = t.DraftText is { Length: > 0 } draft ? $"Draft: {draft}" : t.PreviewText,
            ms = t.LastMessageTimestamp.ToUnixTimeMilliseconds(),
            unread = t.UnreadCount > 0,
            isGroup = t.IsGroup,
            isMuted = t.IsMuted
        }));
    }

    private async Task<WebResponse> MessagesAsync(long threadId, WebRequest request)
    {
        DateTimeOffset? before = long.TryParse(request.Query.GetValueOrDefault("before"), out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
        var messages = (await _sms.GetMessagesAsync(threadId, before, PageSize)).OrderBy(m => m.Timestamp).ToList();
        ReactionAttacher.Apply(messages);

        var names = new Dictionary<string, string>();
        foreach (var address in messages.Where(m => !m.IsOutgoing).Select(m => m.Address).Distinct())
        {
            names[address] = (await _contacts.LookupAsync(address))?.DisplayName ?? PhoneNumberFormatter.ToDisplayFormat(address);
        }

        // Paging follows the raw records: hidden reactions shrink the visible list, not the history.
        var visible = messages.Where(m => !m.IsHiddenReaction).Select(m => new
        {
            id = m.Id,
            ms = m.Timestamp.ToUnixTimeMilliseconds(),
            body = m.Body,
            outgoing = m.IsOutgoing,
            senderName = m.IsOutgoing ? null : names[m.Address],
            status = m.StatusDisplay,
            reactions = m.Reactions,
            attachments = m.Attachments.Select(a => new { partId = a.PartId, kind = a.Kind.ToString().ToLowerInvariant(), name = a.FileName })
        });
        return WebResponse.Json(new
        {
            messages = visible,
            hasMore = messages.Count >= PageSize,
            before = messages.Count > 0 ? messages[0].Timestamp.ToUnixTimeMilliseconds() : (long?)null
        });
    }

    private async Task<WebResponse> MarkReadAsync(long threadId)
    {
        await _markAsRead.MarkThreadAsReadAsync(threadId);
        return WebResponse.Json(new { ok = true });
    }

    private async Task<WebResponse> ReplyAsync(long threadId, WebRequest request)
    {
        var body = JsonSerializer.Deserialize<ReplyBody>(request.Body ?? "{}", JsonOptions);
        if (string.IsNullOrWhiteSpace(body?.Text) || string.IsNullOrWhiteSpace(body.Address))
        {
            return WebResponse.Error(400, "missing-text");
        }
        await _replySender.SendAsync(threadId, body.Address, body.Text);
        return WebResponse.Json(new { ok = true });
    }

    private async Task<WebResponse> NewMessageAsync(WebRequest request)
    {
        var body = JsonSerializer.Deserialize<NewMessageBody>(request.Body ?? "{}", JsonOptions);
        var recipients = body?.To?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList() ?? new List<string>();
        var text = body?.Text?.Trim();
        if (recipients.Count == 0 || string.IsNullOrEmpty(text))
        {
            return WebResponse.Error(400, "missing-recipient-or-text");
        }

        if (recipients.Count == 1)
        {
            await _sms.SendAsync(recipients[0], text);
        }
        else
        {
            await _sms.SendGroupAsync(0, recipients, text, null);
        }
        return WebResponse.Json(new { ok = true });
    }

    private async Task<WebResponse> SearchAsync(WebRequest request)
    {
        var query = request.Query.GetValueOrDefault("q")?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            return WebResponse.Json(Array.Empty<object>());
        }

        var visible = (await _conversations.GetAllAsync()).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        var results = await _sms.SearchAllMessagesAsync(query, MaxSearchResults);
        return WebResponse.Json(results
            .Where(m => visible.ContainsKey(m.ThreadId))
            .OrderByDescending(m => m.Timestamp)
            .Select(m => new
            {
                threadId = m.ThreadId,
                address = visible[m.ThreadId].Address,
                conversationName = visible[m.ThreadId].DisplayNameOrAddress,
                id = m.Id,
                ms = m.Timestamp.ToUnixTimeMilliseconds(),
                body = m.Body,
                outgoing = m.IsOutgoing
            }));
    }

    private async Task<WebResponse> ContactsAsync(WebRequest request)
    {
        var query = request.Query.GetValueOrDefault("q")?.Trim() ?? string.Empty;
        var contacts = await _contacts.GetAllContactsAsync();
        return WebResponse.Json(contacts
            .Where(c => query.Length == 0
                || c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.PhoneNumber.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(c => new { name = c.DisplayName, number = c.PhoneNumber }));
    }

    private async Task<WebResponse> MediaAsync(long partId, WebRequest request)
    {
        var size = int.TryParse(request.Query.GetValueOrDefault("size"), out var requested) ? Math.Clamp(requested, 64, 1600) : 480;
        var dataUri = await _thumbnails.GetImageDataUriAsync(partId, size);
        var comma = dataUri?.IndexOf(',') ?? -1;
        return comma < 0
            ? WebResponse.Error(404, "not-found")
            : new WebResponse(200, "image/jpeg", Convert.FromBase64String(dataUri![(comma + 1)..]));
    }
}
