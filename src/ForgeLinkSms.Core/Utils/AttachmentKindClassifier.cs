using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class AttachmentKindClassifier
{
    public static AttachmentKind FromContentType(string? contentType)
    {
        if (contentType is null)
        {
            return AttachmentKind.File;
        }

        if (contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentKind.Gif;
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentKind.Image;
        }

        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentKind.Video;
        }

        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentKind.Audio;
        }

        if (contentType.Equals("text/x-vcard", StringComparison.OrdinalIgnoreCase) ||
            contentType.Equals("text/vcard", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentKind.Contact;
        }

        return AttachmentKind.File;
    }
}
