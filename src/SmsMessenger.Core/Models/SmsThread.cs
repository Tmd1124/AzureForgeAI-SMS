namespace SmsMessenger.Core.Models;

public class SmsThread
{
    public required long Id { get; init; }
    public required string Address { get; init; }
    public required string? DisplayName { get; init; }
    public required string LastMessageBody { get; init; }
    public required DateTimeOffset LastMessageTimestamp { get; init; }
    public required int UnreadCount { get; init; }

    public string DisplayNameOrAddress => string.IsNullOrWhiteSpace(DisplayName) ? Address : DisplayName;

    public string PreviewText => LastMessageBody.Length > 60
        ? LastMessageBody[..60] + "…"
        : LastMessageBody;
}
