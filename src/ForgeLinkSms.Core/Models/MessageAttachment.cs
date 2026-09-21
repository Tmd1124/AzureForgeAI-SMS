namespace ForgeLinkSms.Core.Models;

public class MessageAttachment
{
    public required string FileName { get; init; }
    public required AttachmentKind Kind { get; init; }

    /// Populated for images/GIFs so the bubble can render a thumbnail inline. Left null for
    /// video/file attachments — decoding those to a base64 data URI would mean holding
    /// multi-megabyte payloads in memory just to show an icon that never uses the bytes.
    public string? DataUri { get; init; }
}
