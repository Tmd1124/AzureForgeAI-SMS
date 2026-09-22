namespace ForgeLinkSms.Core.Models;

public class MessageAttachment
{
    public required string FileName { get; init; }
    public required AttachmentKind Kind { get; init; }

    /// Populated for images/GIFs so the bubble can render a thumbnail inline. Left null for
    /// video/file attachments — decoding those to a base64 data URI would mean holding
    /// multi-megabyte payloads in memory just to show an icon that never uses the bytes.
    public string? DataUri { get; init; }

    /// The MMS part's own row id (content://mms/part/{PartId}), independent of whether DataUri
    /// was inlined at read time. Saving an attachment to the device re-reads the full-quality
    /// bytes from this id on demand rather than re-decoding DataUri — the only way to get bytes
    /// at all for video/file kinds, which never get a DataUri, and for images that were skipped
    /// for being over MmsReader's inline-size cap.
    public required long PartId { get; init; }
}
