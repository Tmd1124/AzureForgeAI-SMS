using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IAttachmentSaveService
{
    /// Saves a received attachment's full-quality bytes to the device's own Photos/Videos/
    /// Downloads collection (re-read fresh from its source part, not from any inlined preview
    /// data). Returns false on any failure rather than throwing, so the caller can show a plain
    /// "couldn't save" toast without needing to inspect an exception.
    Task<bool> SaveToDeviceAsync(MessageAttachment attachment);
}
