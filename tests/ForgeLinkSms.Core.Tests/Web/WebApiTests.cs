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
    [InlineData("127.0.0.1", false)]
    [InlineData("127.0.0.2", false)]
    [InlineData("::1", false)]
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

        var messages = Json(response).GetProperty("messages");
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

        Assert.Equal("older", Json(response).GetProperty("messages")[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task A_full_page_of_mostly_reactions_still_offers_older_messages()
    {
        var token = await PairAsync();
        var page = Enumerable.Range(1, 49).Select(i => Message(i + 1, "Loved “Sounds good”", 1000 + i * 10)).ToList();
        page.Add(Message(1, "Sounds good", 1000, outgoing: true));
        _sms.Setup(s => s.GetMessagesAsync(1, null, 50)).ReturnsAsync(page);

        var result = Json(await MakeApi().HandleAsync(Get("/api/conversations/1/messages", token)));

        Assert.Equal(1, result.GetProperty("messages").GetArrayLength());
        Assert.True(result.GetProperty("hasMore").GetBoolean());
        Assert.Equal(1000, result.GetProperty("before").GetInt64());
    }

    [Fact]
    public async Task A_short_page_means_the_start_of_the_conversation()
    {
        var token = await PairAsync();
        _sms.Setup(s => s.GetMessagesAsync(1, null, 50)).ReturnsAsync(new List<SmsMessage> { Message(1, "hi", 1000) });

        var result = Json(await MakeApi().HandleAsync(Get("/api/conversations/1/messages", token)));

        Assert.False(result.GetProperty("hasMore").GetBoolean());
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
