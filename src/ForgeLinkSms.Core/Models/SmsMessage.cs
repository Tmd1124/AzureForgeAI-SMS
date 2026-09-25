namespace ForgeLinkSms.Core.Models;

public enum SmsMessageStatus
{
    Sending,
    Sent,
    Delivered,
    Failed
}

public class SmsMessage
{
    public required long Id { get; init; }
    public required long ThreadId { get; init; }
    public required string Address { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required bool IsOutgoing { get; init; }
    public required SmsMessageStatus Status { get; init; }

    /// SMS and MMS live in separate tables with overlapping ids, so deleting needs to know which.
    public bool IsMms { get; init; }

    /// Reactions other people sent to this message (from texts like "Loved “…”").
    public List<string> Reactions { get; } = new();

    /// True for a reaction text already shown as a badge on the message it quotes.
    public bool IsHiddenReaction { get; set; }
    public IReadOnlyList<MessageAttachment> Attachments { get; init; } = Array.Empty<MessageAttachment>();

    /// Only outgoing messages have a status to show — SMS has no concept
    /// of a "read" tick, so this never goes past Delivered.
    public string StatusDisplay => (IsOutgoing, Status) switch
    {
        (false, _) => string.Empty,
        (true, SmsMessageStatus.Sending) => "Sending…",
        (true, SmsMessageStatus.Sent) => "✓ Sent",
        (true, SmsMessageStatus.Delivered) => "✓✓ Delivered",
        (true, SmsMessageStatus.Failed) => "⚠ Not sent",
        _ => string.Empty
    };
}
