using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IAttachmentPickerService
{
    Task<PickedAttachment?> PickImageOrVideoAsync();
    Task<PickedAttachment?> PickGifAsync();
    Task<PickedAttachment?> PickFileAsync();
}
