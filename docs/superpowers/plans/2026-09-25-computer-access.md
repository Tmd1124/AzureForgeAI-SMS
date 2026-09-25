# Computer Access (Home Wi-Fi) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a computer on the same Wi-Fi read and send texts through ForgeLink from a web browser.

**Architecture:** Testable Core pieces (`PairingService`, `WebApi`, `WebEventHub`) take plain request/response records and existing services, so they have no Android or socket types. An Android `ComputerAccessServer` (managed `HttpListener`, spike-confirmed) hosts them inside a foreground service. A single-file browser app (`Resources/Raw/web/index.html`) calls the JSON API and listens to a Server-Sent Events stream.

**Tech Stack:** .NET 9 MAUI Blazor Hybrid (Android), System.Net.HttpListener, System.Text.Json, sqlite-net-pcl, xUnit + Moq, vanilla HTML/JS.

**Spec:** `docs/superpowers/specs/2026-09-25-computer-access-design.md`

## Global Constraints

- Port 8765; address shown as `http://<phone-wifi-ipv4>:8765`.
- Pairing code: 6 digits. Tokens: 128-bit random, stored only as SHA-256 hashes, revocable with **Unpair all**.
- Wrong codes: 5 per minute per browser, then a 1-minute lockout.
- Only private-network client addresses are served (10.x, 172.16–31.x, 192.168.x, 169.254.x, loopback, IPv6 link-local/unique-local); refuse to start without Wi-Fi.
- Foreground-service notification text: "ForgeLink is sharing messages with your computer", with a **Turn off** action.
- Browser scope: read conversations (Chats/Updates/Screener), photos, group sender names, reply, new text to contact or number, search, live updates, mark read. No sending photos/voice, scheduling, filters, archive/trash/snooze from the browser.
- Message text from the phone is rendered in the browser as text only (never as HTML).
- **No git commits in any task.** The user commits only on explicit request.

## Review Focus

1. **Message text containing HTML** (`<img src=x onerror=alert(1)>`). Expected: shown literally, never executed. Pinned in Task 6 Step 2 (no `innerHTML` with server data).
2. **A browser on a non-private network address** (phone on a hotspot or a public network). Expected: 403 on every route. Pinned by `Requests_from_public_addresses_are_refused` (Task 2).
3. **Scrolling back through a long conversation.** Expected: older pages load using the oldest shown message. Pinned by `Messages_page_uses_the_before_cursor` (Task 2).
4. **Two browsers paired, then Unpair all.** Expected: both lose access. Pinned by `UnpairAll_revokes_every_browser` (Task 1).
5. **Guessing the code from one browser.** Expected: that browser locks out, a different browser can still pair. Pinned by `Lockout_is_per_browser` (Task 1).

---

### Task 1: Pairing (codes, lockout, tokens)

**Files:**
- Create: `src/ForgeLinkSms.Core/Web/PairedBrowserStore.cs`
- Create: `src/ForgeLinkSms.Core/Web/PairingService.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Web/PairingServiceTests.cs`

**Interfaces:**
- Produces:
  - `IPairedBrowserStore { Task InitializeAsync(); Task AddAsync(string tokenHash); Task<bool> ContainsAsync(string tokenHash); Task<int> CountAsync(); Task ClearAsync(); }`, implemented by `PairedBrowserStore(string databasePath)`
  - `enum PairingOutcome { Paired, WrongCode, LockedOut }`, `record PairingResult(PairingOutcome Outcome, string? Token)`
  - `PairingService(IPairedBrowserStore store, TimeProvider? clock = null)`: `string Code`, `void NewCode()`, `Task<PairingResult> PairAsync(string code, string client)`, `Task<bool> IsAuthorizedAsync(string? token)`, `Task<int> PairedCountAsync()`, `Task UnpairAllAsync()`

- [ ] **Step 1: Write the failing tests**

```csharp
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.Tests.Web;

public class PairingServiceTests
{
    private sealed class MemoryStore : IPairedBrowserStore
    {
        private readonly HashSet<string> _hashes = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task AddAsync(string tokenHash) { _hashes.Add(tokenHash); return Task.CompletedTask; }
        public Task<bool> ContainsAsync(string tokenHash) => Task.FromResult(_hashes.Contains(tokenHash));
        public Task<int> CountAsync() => Task.FromResult(_hashes.Count);
        public Task ClearAsync() { _hashes.Clear(); return Task.CompletedTask; }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly MemoryStore _store = new();
    private readonly TestClock _clock = new();

    private PairingService MakeService() => new(_store, _clock);

    [Fact]
    public void The_code_is_six_digits()
    {
        Assert.Matches("^[0-9]{6}$", MakeService().Code);
    }

    [Fact]
    public async Task The_right_code_pairs_and_the_token_authorizes()
    {
        var service = MakeService();

        var result = await service.PairAsync(service.Code, "192.168.1.130");

        Assert.Equal(PairingOutcome.Paired, result.Outcome);
        Assert.True(await service.IsAuthorizedAsync(result.Token));
        Assert.False(await service.IsAuthorizedAsync("made-up-token"));
        Assert.False(await service.IsAuthorizedAsync(null));
    }

    [Fact]
    public async Task Pairing_changes_the_code_so_it_cannot_be_reused()
    {
        var service = MakeService();
        var code = service.Code;

        await service.PairAsync(code, "192.168.1.130");

        Assert.NotEqual(code, service.Code);
    }

    [Fact]
    public async Task A_wrong_code_is_rejected()
    {
        var service = MakeService();

        var result = await service.PairAsync(service.Code == "000000" ? "111111" : "000000", "192.168.1.130");

        Assert.Equal(PairingOutcome.WrongCode, result.Outcome);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_out_even_the_right_one()
    {
        var service = MakeService();
        var wrong = service.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await service.PairAsync(wrong, "192.168.1.130");
        }

        var result = await service.PairAsync(service.Code, "192.168.1.130");

        Assert.Equal(PairingOutcome.LockedOut, result.Outcome);
    }

    [Fact]
    public async Task The_lockout_ends_after_a_minute()
    {
        var service = MakeService();
        var wrong = service.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await service.PairAsync(wrong, "192.168.1.130");
        }

        _clock.Now = _clock.Now.AddSeconds(61);

        Assert.Equal(PairingOutcome.Paired, (await service.PairAsync(service.Code, "192.168.1.130")).Outcome);
    }

    [Fact]
    public async Task Lockout_is_per_browser()
    {
        var service = MakeService();
        var wrong = service.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await service.PairAsync(wrong, "192.168.1.130");
        }

        Assert.Equal(PairingOutcome.Paired, (await service.PairAsync(service.Code, "192.168.1.44")).Outcome);
    }

    [Fact]
    public async Task UnpairAll_revokes_every_browser()
    {
        var service = MakeService();
        var first = (await service.PairAsync(service.Code, "192.168.1.130")).Token;
        var second = (await service.PairAsync(service.Code, "192.168.1.44")).Token;
        Assert.Equal(2, await service.PairedCountAsync());

        await service.UnpairAllAsync();

        Assert.False(await service.IsAuthorizedAsync(first));
        Assert.False(await service.IsAuthorizedAsync(second));
        Assert.Equal(0, await service.PairedCountAsync());
    }

    [Fact]
    public async Task A_paired_browser_stays_paired_after_the_app_restarts()
    {
        var before = MakeService();
        var token = (await before.PairAsync(before.Code, "192.168.1.130")).Token;

        var after = MakeService();

        Assert.True(await after.IsAuthorizedAsync(token));
    }

    [Fact]
    public async Task PairedBrowserStore_persists_hashes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"paired-{Guid.NewGuid()}.db3");
        using (var store = new PairedBrowserStore(path))
        {
            await store.InitializeAsync();
            await store.AddAsync("abc");
            Assert.True(await store.ContainsAsync("abc"));
            Assert.Equal(1, await store.CountAsync());
            await store.ClearAsync();
            Assert.False(await store.ContainsAsync("abc"));
        }
        File.Delete(path);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~PairingServiceTests"`
Expected: build error `The type or namespace name 'Web' does not exist`.

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Web/PairedBrowserStore.cs`:

```csharp
using SQLite;

namespace ForgeLinkSms.Core.Web;

public class PairedBrowser
{
    [PrimaryKey]
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset PairedAtUtc { get; set; }
}

public interface IPairedBrowserStore
{
    Task InitializeAsync();
    Task AddAsync(string tokenHash);
    Task<bool> ContainsAsync(string tokenHash);
    Task<int> CountAsync();
    Task ClearAsync();
}

public class PairedBrowserStore : IPairedBrowserStore, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public PairedBrowserStore(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<PairedBrowser>();

    public Task AddAsync(string tokenHash) =>
        _db.InsertOrReplaceAsync(new PairedBrowser { TokenHash = tokenHash, PairedAtUtc = DateTimeOffset.UtcNow });

    public async Task<bool> ContainsAsync(string tokenHash) =>
        await _db.FindAsync<PairedBrowser>(tokenHash).ConfigureAwait(false) is not null;

    public Task<int> CountAsync() => _db.Table<PairedBrowser>().CountAsync();

    public Task ClearAsync() => _db.DeleteAllAsync<PairedBrowser>();

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
```

`src/ForgeLinkSms.Core/Web/PairingService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ForgeLinkSms.Core.Web;

public enum PairingOutcome
{
    Paired,
    WrongCode,
    LockedOut
}

public sealed record PairingResult(PairingOutcome Outcome, string? Token);

// Pairs a browser with the code shown on the phone. Browsers keep a random token (only its hash
// is stored), so pairing survives app restarts and can be revoked for everyone at once.
public class PairingService
{
    public const int MaxWrongCodes = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly IPairedBrowserStore _store;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _wrongAttempts = new();
    private readonly Dictionary<string, DateTimeOffset> _lockedUntil = new();

    public PairingService(IPairedBrowserStore store, TimeProvider? clock = null)
    {
        _store = store;
        _clock = clock ?? TimeProvider.System;
        NewCode();
    }

    public string Code { get; private set; } = string.Empty;

    public void NewCode() => Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public async Task<PairingResult> PairAsync(string code, string client)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (_lockedUntil.TryGetValue(client, out var until) && until > now)
            {
                return new PairingResult(PairingOutcome.LockedOut, null);
            }

            if (code.Trim() != Code)
            {
                var attempts = _wrongAttempts.TryGetValue(client, out var list) ? list : _wrongAttempts[client] = new List<DateTimeOffset>();
                attempts.RemoveAll(t => now - t > Window);
                attempts.Add(now);
                if (attempts.Count >= MaxWrongCodes)
                {
                    _lockedUntil[client] = now + Window;
                    attempts.Clear();
                }
                return new PairingResult(PairingOutcome.WrongCode, null);
            }

            _wrongAttempts.Remove(client);
            _lockedUntil.Remove(client);
            // A used code can't pair a second browser; the phone shows the new one.
            NewCode();
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await _store.AddAsync(Hash(token)).ConfigureAwait(false);
        return new PairingResult(PairingOutcome.Paired, token);
    }

    public async Task<bool> IsAuthorizedAsync(string? token) =>
        !string.IsNullOrEmpty(token) && await _store.ContainsAsync(Hash(token)).ConfigureAwait(false);

    public Task<int> PairedCountAsync() => _store.CountAsync();

    public Task UnpairAllAsync() => _store.ClearAsync();

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~PairingServiceTests"`
Expected: all pass.

---

### Task 2: WebApi (JSON routes, auth, private-network check)

**Files:**
- Create: `src/ForgeLinkSms.Core/Web/WebMessages.cs` (request/response records, `IStaticFiles`, `PrivateNetwork`)
- Create: `src/ForgeLinkSms.Core/Web/ConversationSource.cs`
- Create: `src/ForgeLinkSms.Core/Web/WebApi.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Web/WebApiTests.cs`

**Interfaces:**
- Consumes: `PairingService` (Task 1); existing `ISmsService`, `IThreadService`, `IMarkAsReadService`, `IMediaThumbnailService`, `IContactService`, `ConversationReplySender`, `ConversationsViewModel`, `ReactionAttacher`.
- Produces:
  - `record WebRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query, string? Body, string? Token, string ClientAddress)`
  - `record WebResponse(int Status, string ContentType, byte[] Body)` with statics `Json(object value, int status = 200)`, `Error(int status, string code)`
  - `interface IStaticFiles { Task<(byte[] Content, string ContentType)?> GetAsync(string path); }`
  - `static class PrivateNetwork { static bool IsPrivate(string address); }`
  - `interface IConversationSource { Task<IReadOnlyList<SmsThread>> GetAsync(ConversationLane lane); Task<IReadOnlyList<SmsThread>> GetAllAsync(); }`, implemented by `ConversationSource(Func<ConversationsViewModel> create)`
  - `WebApi(PairingService pairing, IConversationSource conversations, ISmsService sms, IThreadService threads, IMarkAsReadService markAsRead, IMediaThumbnailService thumbnails, IContactService contacts, IStaticFiles staticFiles)` with `Task<WebResponse> HandleAsync(WebRequest request)` and `static JsonSerializerOptions JsonOptions`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using System.Text.Json;
using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.Tests.Web;

public class WebApiTests
{
    private sealed class MemoryStore : IPairedBrowserStore
    {
        private readonly HashSet<string> _hashes = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task AddAsync(string tokenHash) { _hashes.Add(tokenHash); return Task.CompletedTask; }
        public Task<bool> ContainsAsync(string tokenHash) => Task.FromResult(_hashes.Contains(tokenHash));
        public Task<int> CountAsync() => Task.FromResult(_hashes.Count);
        public Task ClearAsync() { _hashes.Clear(); return Task.CompletedTask; }
    }

    private readonly PairingService _pairing = new(new MemoryStore());
    private readonly Mock<IConversationSource> _conversations = new();
    private readonly Mock<ISmsService> _sms = new();
    private readonly Mock<IThreadService> _threads = new();
    private readonly Mock<IMarkAsReadService> _markAsRead = new();
    private readonly Mock<IMediaThumbnailService> _thumbnails = new();
    private readonly Mock<IContactService> _contacts = new();
    private readonly Mock<IStaticFiles> _static = new();

    private WebApi MakeApi() => new(_pairing, _conversations.Object, _sms.Object, _threads.Object, _markAsRead.Object, _thumbnails.Object, _contacts.Object, _static.Object);

    private async Task<string> PairAsync() => (await _pairing.PairAsync(_pairing.Code, "192.168.1.130")).Token!;

    private static WebRequest Get(string path, string? token, Dictionary<string, string>? query = null, string client = "192.168.1.130") =>
        new("GET", path, query ?? new Dictionary<string, string>(), null, token, client);

    private static WebRequest Post(string path, string? token, object body) =>
        new("POST", path, new Dictionary<string, string>(), JsonSerializer.Serialize(body, WebApi.JsonOptions), token, "192.168.1.130");

    private static JsonElement Json(WebResponse response) => JsonDocument.Parse(response.Body).RootElement;

    private static SmsThread Thread(long id, string? name, string address, string preview) => new()
    {
        Id = id,
        Address = address,
        DisplayName = name,
        LastMessageBody = preview,
        LastMessageTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000 + id),
        UnreadCount = id == 1 ? 1 : 0
    };

    private static SmsMessage Message(long id, string body, long ms, bool outgoing = false, string address = "555") => new()
    {
        Id = id,
        ThreadId = 1,
        Address = address,
        Body = body,
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ms),
        IsOutgoing = outgoing,
        Status = SmsMessageStatus.Delivered
    };

    [Fact]
    public async Task The_web_page_is_served_without_pairing()
    {
        _static.Setup(s => s.GetAsync("index.html")).ReturnsAsync((Encoding.UTF8.GetBytes("<html>"), "text/html"));

        var response = await MakeApi().HandleAsync(Get("/", token: null));

        Assert.Equal(200, response.Status);
        Assert.Equal("text/html", response.ContentType);
    }

    [Fact]
    public async Task Requests_from_public_addresses_are_refused()
    {
        var token = await PairAsync();

        var response = await MakeApi().HandleAsync(Get("/api/conversations", token, client: "8.8.8.8"));

        Assert.Equal(403, response.Status);
    }

    [Theory]
    [InlineData("192.168.1.5", true)]
    [InlineData("10.0.0.7", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("172.32.1.1", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::ffff:192.168.1.5", true)]
    [InlineData("fe80::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2001:4860::1", false)]
    public void PrivateNetwork_recognizes_home_network_addresses(string address, bool expected)
    {
        Assert.Equal(expected, PrivateNetwork.IsPrivate(address));
    }

    [Fact]
    public async Task Pairing_with_the_right_code_returns_a_token()
    {
        var response = await MakeApi().HandleAsync(Post("/api/pair", null, new { code = _pairing.Code }));

        Assert.Equal(200, response.Status);
        Assert.True(await _pairing.IsAuthorizedAsync(Json(response).GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Pairing_with_a_wrong_code_is_rejected()
    {
        var wrong = _pairing.Code == "000000" ? "111111" : "000000";

        var response = await MakeApi().HandleAsync(Post("/api/pair", null, new { code = wrong }));

        Assert.Equal(401, response.Status);
        Assert.Equal("wrong-code", Json(response).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Api_routes_require_pairing()
    {
        var response = await MakeApi().HandleAsync(Get("/api/conversations", token: "not-paired"));

        Assert.Equal(401, response.Status);
    }

    [Fact]
    public async Task Conversations_lists_the_requested_lane()
    {
        var token = await PairAsync();
        _conversations.Setup(c => c.GetAsync(ConversationLane.Updates)).ReturnsAsync(new List<SmsThread> { Thread(1, null, "72975", "Your code is 1234") });

        var response = await MakeApi().HandleAsync(Get("/api/conversations", token, new() { ["lane"] = "updates" }));

        var first = Json(response)[0];
        Assert.Equal(1, first.GetProperty("id").GetInt64());
        Assert.Equal("72975", first.GetProperty("name").GetString());
        Assert.Equal("Your code is 1234", first.GetProperty("preview").GetString());
        Assert.True(first.GetProperty("unread").GetBoolean());
    }

    [Fact]
    public async Task Messages_hides_reaction_texts_and_returns_them_as_reactions()
    {
        var token = await PairAsync();
        _sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            Message(2, "Loved “Sounds good”", 2000),
            Message(1, "Sounds good", 1000, outgoing: true)
        });

        var response = await MakeApi().HandleAsync(Get("/api/conversations/1/messages", token));

        var messages = Json(response);
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("Sounds good", messages[0].GetProperty("body").GetString());
        Assert.Equal("❤️", messages[0].GetProperty("reactions")[0].GetString());
    }

    [Fact]
    public async Task Messages_page_uses_the_before_cursor()
    {
        var token = await PairAsync();
        _sms.Setup(s => s.GetMessagesAsync(1, DateTimeOffset.FromUnixTimeMilliseconds(5000), It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { Message(1, "older", 1000) });

        var response = await MakeApi().HandleAsync(Get("/api/conversations/1/messages", token, new() { ["before"] = "5000" }));

        Assert.Equal("older", Json(response)[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Reply_sends_through_the_phone()
    {
        var token = await PairAsync();
        _threads.Setup(t => t.GetParticipantsAsync(1)).ReturnsAsync(new[] { "5550148890" });

        var response = await MakeApi().HandleAsync(Post("/api/conversations/1/reply", token, new { address = "5550148890", text = "On my way" }));

        Assert.Equal(200, response.Status);
        _sms.Verify(s => s.SendAsync("5550148890", "On my way"), Times.Once);
    }

    [Fact]
    public async Task A_new_message_to_several_people_is_a_group_message()
    {
        var token = await PairAsync();

        var response = await MakeApi().HandleAsync(Post("/api/messages/new", token, new { to = new[] { "5550148890", "5550142231" }, text = "Dinner?" }));

        Assert.Equal(200, response.Status);
        _sms.Verify(s => s.SendGroupAsync(0, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "5550148890", "5550142231" })), "Dinner?", null), Times.Once);
    }

    [Fact]
    public async Task A_new_message_without_text_is_a_bad_request()
    {
        var token = await PairAsync();

        var response = await MakeApi().HandleAsync(Post("/api/messages/new", token, new { to = new[] { "5550148890" }, text = " " }));

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Search_names_each_result_and_skips_hidden_conversations()
    {
        var token = await PairAsync();
        _conversations.Setup(c => c.GetAllAsync()).ReturnsAsync(new List<SmsThread> { Thread(1, "Mom", "555", "hi") });
        var trashedThreadMessage = Message(9, "dinner is ready", 3000) ;
        _sms.Setup(s => s.SearchAllMessagesAsync("dinner", It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            Message(8, "dinner?", 2000),
            new SmsMessage { Id = 9, ThreadId = 99, Address = "556", Body = trashedThreadMessage.Body, Timestamp = trashedThreadMessage.Timestamp, IsOutgoing = false, Status = SmsMessageStatus.Delivered }
        });

        var response = await MakeApi().HandleAsync(Get("/api/search", token, new() { ["q"] = "dinner" }));

        var results = Json(response);
        Assert.Equal(1, results.GetArrayLength());
        Assert.Equal("Mom", results[0].GetProperty("conversationName").GetString());
        Assert.Equal(1, results[0].GetProperty("threadId").GetInt64());
    }

    [Fact]
    public async Task Media_returns_the_photo_as_jpeg()
    {
        var token = await PairAsync();
        _thumbnails.Setup(t => t.GetImageDataUriAsync(42, 480)).ReturnsAsync("data:image/jpeg;base64," + Convert.ToBase64String(new byte[] { 1, 2, 3 }));

        var response = await MakeApi().HandleAsync(Get("/api/media/42", token, new() { ["size"] = "480" }));

        Assert.Equal(200, response.Status);
        Assert.Equal("image/jpeg", response.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, response.Body);
    }

    [Fact]
    public async Task Unknown_routes_are_not_found()
    {
        var token = await PairAsync();

        Assert.Equal(404, (await MakeApi().HandleAsync(Get("/api/nope", token))).Status);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~WebApiTests"`
Expected: build error `The type or namespace name 'WebApi' could not be found`.

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Web/WebMessages.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ForgeLinkSms.Core.Web;

public sealed record WebRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query, string? Body, string? Token, string ClientAddress);

public sealed record WebResponse(int Status, string ContentType, byte[] Body)
{
    public static WebResponse Json(object value, int status = 200) =>
        new(status, "application/json", JsonSerializer.SerializeToUtf8Bytes(value, WebApi.JsonOptions));

    public static WebResponse Error(int status, string code) => Json(new { error = code }, status);
}

public interface IStaticFiles
{
    Task<(byte[] Content, string ContentType)?> GetAsync(string path);
}

public static class PrivateNetwork
{
    public static bool IsPrivate(string address)
    {
        if (!IPAddress.TryParse(address, out var ip))
        {
            return false;
        }
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
        }
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }
}
```

`src/ForgeLinkSms.Core/Web/ConversationSource.cs`:

```csharp
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Web;

public interface IConversationSource
{
    Task<IReadOnlyList<SmsThread>> GetAsync(ConversationLane lane);

    /// Every conversation the phone's list shows in any lane (not trashed, archived, blocked, or snoozed).
    Task<IReadOnlyList<SmsThread>> GetAllAsync();
}

// Reuses the phone's own conversation-list logic so the browser sees exactly the same lists.
public class ConversationSource : IConversationSource
{
    private readonly Func<ConversationsViewModel> _create;

    public ConversationSource(Func<ConversationsViewModel> create)
    {
        _create = create;
    }

    public async Task<IReadOnlyList<SmsThread>> GetAsync(ConversationLane lane)
    {
        var viewModel = _create();
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Lane = lane;
        return viewModel.Threads.ToList();
    }

    public async Task<IReadOnlyList<SmsThread>> GetAllAsync()
    {
        var all = new List<SmsThread>();
        foreach (var lane in Enum.GetValues<ConversationLane>())
        {
            all.AddRange(await GetAsync(lane));
        }
        return all;
    }
}
```

`src/ForgeLinkSms.Core/Web/WebApi.cs`:

```csharp
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

        return WebResponse.Json(messages.Where(m => !m.IsHiddenReaction).Select(m => new
        {
            id = m.Id,
            ms = m.Timestamp.ToUnixTimeMilliseconds(),
            body = m.Body,
            outgoing = m.IsOutgoing,
            senderName = m.IsOutgoing ? null : names[m.Address],
            status = m.StatusDisplay,
            reactions = m.Reactions,
            attachments = m.Attachments.Select(a => new { partId = a.PartId, kind = a.Kind.ToString().ToLowerInvariant(), name = a.FileName })
        }));
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~WebApiTests"`
Expected: all pass.

---

### Task 3: Live events hub

**Files:**
- Create: `src/ForgeLinkSms.Core/Web/WebEventHub.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Web/WebEventHubTests.cs`

**Interfaces:**
- Consumes: existing `IIncomingMessageNotifier` (`event Action<long>? MessageReceived`).
- Produces: `WebEventHub(IIncomingMessageNotifier notifier)` with `(Guid Id, ChannelReader<string> Events) Subscribe()`, `void Unsubscribe(Guid id)`, `void Publish(string json)`, `int SubscriberCount`, `IDisposable`.

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~WebEventHubTests"`
Expected: build error `The type or namespace name 'WebEventHub' could not be found`.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~WebEventHubTests"`
Expected: all pass.

---

### Task 4: Computer access screen logic (Core)

**Files:**
- Create: `src/ForgeLinkSms.Core/Web/IComputerAccessController.cs`
- Create: `src/ForgeLinkSms.Core/ViewModels/ComputerAccessViewModel.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/ComputerAccessViewModelTests.cs`

**Interfaces:**
- Consumes: `PairingService` (Task 1).
- Produces:
  - `IComputerAccessController { bool IsRunning { get; } string? Address { get; } Task<bool> StartAsync(); Task StopAsync(); event Action? StateChanged; }` (`StartAsync` returns false when the phone isn't on Wi-Fi)
  - `ComputerAccessViewModel(IComputerAccessController controller, PairingService pairing)`: `bool IsRunning`, `string? Address`, `string Code`, `int PairedCount`, `string? Message`; commands `RefreshCommand`, `ToggleCommand`, `UnpairAllCommand`

- [ ] **Step 1: Write the failing tests**

```csharp
using Moq;
using ForgeLinkSms.Core.ViewModels;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ComputerAccessViewModelTests
{
    private sealed class MemoryStore : IPairedBrowserStore
    {
        private readonly HashSet<string> _hashes = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task AddAsync(string tokenHash) { _hashes.Add(tokenHash); return Task.CompletedTask; }
        public Task<bool> ContainsAsync(string tokenHash) => Task.FromResult(_hashes.Contains(tokenHash));
        public Task<int> CountAsync() => Task.FromResult(_hashes.Count);
        public Task ClearAsync() { _hashes.Clear(); return Task.CompletedTask; }
    }

    private readonly Mock<IComputerAccessController> _controller = new();
    private readonly PairingService _pairing = new(new MemoryStore());

    [Fact]
    public async Task Turning_it_on_shows_the_address_and_code()
    {
        _controller.Setup(c => c.StartAsync()).ReturnsAsync(true).Callback(() =>
        {
            _controller.SetupGet(c => c.IsRunning).Returns(true);
            _controller.SetupGet(c => c.Address).Returns("http://192.168.1.220:8765");
        });
        var viewModel = new ComputerAccessViewModel(_controller.Object, _pairing);

        await viewModel.ToggleCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsRunning);
        Assert.Equal("http://192.168.1.220:8765", viewModel.Address);
        Assert.Equal(_pairing.Code, viewModel.Code);
        Assert.Null(viewModel.Message);
    }

    [Fact]
    public async Task Turning_it_on_without_wifi_explains_why()
    {
        _controller.Setup(c => c.StartAsync()).ReturnsAsync(false);
        var viewModel = new ComputerAccessViewModel(_controller.Object, _pairing);

        await viewModel.ToggleCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRunning);
        Assert.Equal("Connect your phone to Wi-Fi first.", viewModel.Message);
    }

    [Fact]
    public async Task Turning_it_off_stops_the_server()
    {
        _controller.SetupGet(c => c.IsRunning).Returns(true);
        var viewModel = new ComputerAccessViewModel(_controller.Object, _pairing);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.ToggleCommand.ExecuteAsync(null);

        _controller.Verify(c => c.StopAsync(), Times.Once);
    }

    [Fact]
    public async Task UnpairAll_resets_the_count()
    {
        await _pairing.PairAsync(_pairing.Code, "192.168.1.130");
        var viewModel = new ComputerAccessViewModel(_controller.Object, _pairing);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, viewModel.PairedCount);

        await viewModel.UnpairAllCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.PairedCount);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter "FullyQualifiedName~ComputerAccessViewModelTests"`
Expected: build error `The type or namespace name 'IComputerAccessController' could not be found`.

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Web/IComputerAccessController.cs`:

```csharp
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
```

`src/ForgeLinkSms.Core/ViewModels/ComputerAccessViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ComputerAccessViewModel : ObservableObject
{
    private readonly IComputerAccessController _controller;
    private readonly PairingService _pairing;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _address;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private int _pairedCount;

    [ObservableProperty]
    private string? _message;

    public ComputerAccessViewModel(IComputerAccessController controller, PairingService pairing)
    {
        _controller = controller;
        _pairing = pairing;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        IsRunning = _controller.IsRunning;
        Address = _controller.Address;
        Code = _pairing.Code;
        PairedCount = await _pairing.PairedCountAsync();
    }

    [RelayCommand]
    private async Task Toggle()
    {
        Message = null;
        if (_controller.IsRunning)
        {
            await _controller.StopAsync();
        }
        else if (!await _controller.StartAsync())
        {
            Message = "Connect your phone to Wi-Fi first.";
        }
        await Refresh();
    }

    [RelayCommand]
    private async Task UnpairAll()
    {
        await _pairing.UnpairAllAsync();
        await Refresh();
    }
}
```

- [ ] **Step 4: Run all Core tests**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests`
Expected: all pass.

---

### Task 5: Android server, foreground service, phone screen

**Files:**
- Create: `src/ForgeLinkSms/Platforms/Android/ComputerAccessServer.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/ComputerAccessService.cs` (foreground service + `ComputerAccessController`)
- Create: `src/ForgeLinkSms/Platforms/Android/AppPackageStaticFiles.cs`
- Create: `src/ForgeLinkSms/Pages/ComputerAccess/ComputerAccessPage.razor`
- Create: `src/ForgeLinkSms/Resources/Raw/web/index.html` (placeholder page, replaced in Task 6)
- Modify: `src/ForgeLinkSms/Platforms/Android/MainApplication.cs` (permissions), `src/ForgeLinkSms/MauiProgram.cs` (DI), `src/ForgeLinkSms/Pages/Menu/MenuPage.razor` (menu entry)

**Interfaces:**
- Consumes: `WebApi`, `WebEventHub`, `PairingService`, `IStaticFiles`, `IComputerAccessController`, `ComputerAccessViewModel` (Tasks 1–4).
- Produces: `ComputerAccessServer.Start(WebApi api, WebEventHub events, PairingService pairing)` / `Stop()`; `ComputerAccessController : IComputerAccessController`; route `/computer-access`.

- [ ] **Step 1: Add the server**

`src/ForgeLinkSms/Platforms/Android/ComputerAccessServer.cs`:

```csharp
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

            string? body = null;
            if (request.HasEntityBody && request.ContentLength64 <= MaxBodyBytes)
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                body = await reader.ReadToEndAsync(stopping);
            }

            var result = await api.HandleAsync(new WebRequest(request.HttpMethod, path, query, body, token, client));
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
```

- [ ] **Step 2: Add the static files, foreground service, and controller**

`src/ForgeLinkSms/Platforms/Android/AppPackageStaticFiles.cs`:

```csharp
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Platforms.Android;

// Serves the browser app bundled at Resources/Raw/web/.
public class AppPackageStaticFiles : IStaticFiles
{
    public async Task<(byte[] Content, string ContentType)?> GetAsync(string path)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync($"web/{path}");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var contentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
            return (buffer.ToArray(), contentType);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or Java.IO.FileNotFoundException)
        {
            return null;
        }
    }
}
```

`src/ForgeLinkSms/Platforms/Android/ComputerAccessService.cs`:

```csharp
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
        Server.Start(services.GetRequiredService<WebApi>(), services.GetRequiredService<WebEventHub>(), services.GetRequiredService<PairingService>());

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
```

Placeholder `src/ForgeLinkSms/Resources/Raw/web/index.html` (replaced in Task 6):

```html
<!doctype html><html><head><meta charset="utf-8"><title>ForgeLink</title></head><body>ForgeLink computer access is running.</body></html>
```

- [ ] **Step 3: Permissions, DI, menu, and phone screen**

In `MainApplication.cs`, after the `RecordAudio` permission line:

```csharp
[assembly: UsesPermission(Android.Manifest.Permission.ForegroundService)]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE")]
[assembly: UsesPermission(Android.Manifest.Permission.ChangeNetworkState)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessWifiState)]
[assembly: UsesPermission(Android.Manifest.Permission.WakeLock)]
```

In `MauiProgram.cs`, after the `LinkPreviewService` registration:

```csharp
		var pairedBrowserStore = new ForgeLinkSms.Core.Web.PairedBrowserStore(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		pairedBrowserStore.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<ForgeLinkSms.Core.Web.IPairedBrowserStore>(pairedBrowserStore);
		builder.Services.AddSingleton(sp => new ForgeLinkSms.Core.Web.PairingService(sp.GetRequiredService<ForgeLinkSms.Core.Web.IPairedBrowserStore>()));
		builder.Services.AddSingleton<ForgeLinkSms.Core.Web.WebEventHub>();
		builder.Services.AddSingleton<ForgeLinkSms.Core.Web.IStaticFiles, AppPackageStaticFiles>();
		builder.Services.AddSingleton<ForgeLinkSms.Core.Web.IConversationSource>(sp => new ForgeLinkSms.Core.Web.ConversationSource(() => sp.GetRequiredService<ConversationsViewModel>()));
		builder.Services.AddSingleton<ForgeLinkSms.Core.Web.WebApi>();
		builder.Services.AddSingleton<ForgeLinkSms.Core.Web.IComputerAccessController, ComputerAccessController>();
		builder.Services.AddTransient<ComputerAccessViewModel>();
```

In `MenuPage.razor`, after the Snoozed entry:

```razor
    <div @onclick="@(() => Nav.NavigateTo("/computer-access"))" style="padding:12px 0;border-bottom:1px solid #eee;">
        💻 Computer access
    </div>
```

`src/ForgeLinkSms/Pages/ComputerAccess/ComputerAccessPage.razor`:

```razor
@page "/computer-access"
@implements IDisposable
@inject ForgeLinkSms.Core.ViewModels.ComputerAccessViewModel ViewModel
@inject ForgeLinkSms.Core.Web.IComputerAccessController Controller

<div style="padding:16px;display:flex;flex-direction:column;gap:16px;">
    <h3 style="margin:0;">💻 Computer access</h3>
    <p style="margin:0;color:#94a3b8;">Read and send your texts from a web browser on a computer that's on the same Wi-Fi as this phone.</p>

    <button @onclick="() => ViewModel.ToggleCommand.ExecuteAsync(null)"
            style="border:none;border-radius:12px;padding:14px;font-size:1.05em;font-weight:600;background:@(ViewModel.IsRunning ? "#ef4444" : "var(--accent-color)");color:white;">
        @(ViewModel.IsRunning ? "Turn off" : "Turn on")
    </button>

    @if (ViewModel.Message is not null)
    {
        <p style="margin:0;color:#ef4444;">@ViewModel.Message</p>
    }

    @if (ViewModel.IsRunning)
    {
        <div style="background:var(--card-color);border-radius:12px;padding:16px;display:flex;flex-direction:column;gap:12px;">
            <div>
                <div style="color:#94a3b8;font-size:0.85em;">1. On your computer, open</div>
                <div style="font-family:monospace;font-size:1.25em;user-select:all;-webkit-user-select:all;">@ViewModel.Address</div>
            </div>
            <div>
                <div style="color:#94a3b8;font-size:0.85em;">2. Enter this code</div>
                <div style="font-family:monospace;font-size:2.2em;letter-spacing:0.2em;">@ViewModel.Code</div>
            </div>
        </div>
        <p style="margin:0;color:#b45309;font-size:0.9em;">⚠️ Only use this on your home Wi-Fi. On public Wi-Fi (hotels, cafés), someone else on the network could see your messages.</p>
    }

    <div style="display:flex;align-items:center;justify-content:space-between;">
        <span>Paired browsers: <strong>@ViewModel.PairedCount</strong></span>
        @if (ViewModel.PairedCount > 0)
        {
            <button @onclick="() => ViewModel.UnpairAllCommand.ExecuteAsync(null)"
                    style="border:none;border-radius:8px;padding:8px 12px;background:#e2e8f0;color:#b91c1c;font-weight:600;">Unpair all</button>
        }
    </div>
</div>

@code {
    private CancellationTokenSource? _refreshCts;

    protected override async Task OnInitializedAsync()
    {
        Controller.StateChanged += OnStateChanged;
        await ViewModel.RefreshCommand.ExecuteAsync(null);

        // The code changes after each pairing and the paired count grows, so keep the screen current.
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    await InvokeAsync(async () =>
                    {
                        await ViewModel.RefreshCommand.ExecuteAsync(null);
                        StateHasChanged();
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void OnStateChanged() => _ = InvokeAsync(async () =>
    {
        await ViewModel.RefreshCommand.ExecuteAsync(null);
        StateHasChanged();
    });

    public void Dispose()
    {
        Controller.StateChanged -= OnStateChanged;
        _refreshCts?.Cancel();
    }
}
```

- [ ] **Step 4: Build, install, verify on the phone and from this PC**

Run (from `src/ForgeLinkSms`): `dotnet build -f net9.0-android -t:SignAndroidPackage -p:EmbedAssembliesIntoApk=true` → Expected: `Build succeeded.`; install with `adb install -r`.
On the phone: Menu → Computer access → Turn on. Expected: address and code shown; the "ForgeLink is sharing messages with your computer" notification appears with **Turn off**.
From this PC: `curl -s http://<phone-ip>:8765/` → placeholder page; `curl -s http://<phone-ip>:8765/api/conversations` → `{"error":"unpaired"}` (401); `curl -s -X POST -d '{"code":"<code>"}' http://<phone-ip>:8765/api/pair` → token; `curl -s -H "Authorization: Bearer <token>" http://<phone-ip>:8765/api/conversations` → JSON list. Tap **Turn off** in the notification → curl fails to connect.

---

### Task 6: Browser app

**Files:**
- Modify (replace): `src/ForgeLinkSms/Resources/Raw/web/index.html`

**Interfaces:**
- Consumes: the Task 2 routes and the Task 5 `/api/events` stream.

- [ ] **Step 1: Write the page**

A single self-contained file (inline CSS/JS). Required behavior:

- Token in `localStorage['forgelink_token']` (wrapped in try/catch). No token or any 401 → pairing screen: 6-digit input, POST `/api/pair`, messages for `wrong-code` ("That code didn't match — check the phone") and `locked` ("Too many tries — wait a minute").
- Two-column layout (single column under 700px): left = lane tabs (Chats/Updates/Screener), search box, **New message** button, conversation list (name, preview, relative time, unread dot, 🔕 for muted); right = header (name), message list, reply box (Enter sends, Shift+Enter newline).
- Opening a conversation: GET messages, POST `/read`; scroll to bottom; loading older pages when scrolled to the top using `before=<oldest ms>`.
- Messages: bubbles (outgoing right, accent colour), sender name above incoming group messages, status under outgoing, reactions as a small pill, photos as `<img src="/api/media/{partId}?size=480&token=…">` (click opens `size=1600` full-size overlay), other attachments as a 📎 name label.
- Search (≥2 chars): results under the list; clicking one opens that conversation.
- New message dialog: recipient search via `/api/contacts?q=`, add typed numbers, multiple recipients (group message), text, Send → POST `/api/messages/new`.
- Live updates: `EventSource('/api/events?token=…')`; on a message event refresh the list and the open conversation if it's the same thread; also refresh every 30 s as a fallback; show a small "Reconnecting…" banner when the stream is down.
- All server data is written with `textContent` / `createElement`; `innerHTML` is only used for static markup with no server data.
- Light and dark themes via `prefers-color-scheme`.

(Full file content is written during execution; it is plain HTML/CSS/JS with no build step and no external requests.)

- [ ] **Step 2: Check rendering safety**

Run: `grep -n "innerHTML" src/ForgeLinkSms/Resources/Raw/web/index.html`
Expected: every match assigns a string literal only (no `${…}` interpolation of server fields).

- [ ] **Step 3: Build, install, and test end to end from this PC's browser**

Install as in Task 5 Step 4. Turn on Computer access, read the address and code from the phone screen, open the address in a browser on this PC (Playwright), pair, and check: the conversation list loads for each tab; a conversation opens with photos and reactions; scrolling up loads older messages; search finds a message and opens its conversation; the live stream connects (DevTools: `/api/events` open). Sending is checked by the user (it sends a real text).
