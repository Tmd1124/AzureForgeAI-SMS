namespace ForgeLinkSms.Core.Models;

/// A photo or video from a conversation, identified by its MMS part so it can be loaded on demand.
public sealed record SharedMedia(long PartId, AttachmentKind Kind, string FileName, DateTimeOffset Timestamp, bool IsOutgoing);

/// A link from a conversation: Text as it appeared, Url ready to open.
public sealed record SharedLink(string Text, string Url, string Domain, DateTimeOffset Timestamp, bool IsOutgoing);
