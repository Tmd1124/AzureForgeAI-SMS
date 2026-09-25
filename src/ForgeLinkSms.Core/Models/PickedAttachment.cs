namespace ForgeLinkSms.Core.Models;

public enum AttachmentKind
{
    Image,
    Video,
    Gif,
    File,
    Contact,
    Audio
}

public class PickedAttachment
{
    public required string FileName { get; init; }
    public required string LocalPath { get; init; }
    public required AttachmentKind Kind { get; init; }
}
