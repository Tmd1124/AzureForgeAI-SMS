using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

// Carries a message being forwarded from the conversation screen to the New Message screen.
public class ForwardRequestStore
{
    private (string? Text, PickedAttachment? Attachment)? _pending;

    public void Set(string? text, PickedAttachment? attachment) => _pending = (text, attachment);

    public (string? Text, PickedAttachment? Attachment)? Take()
    {
        var pending = _pending;
        _pending = null;
        return pending;
    }
}
