namespace ForgeLinkSms.Core.Services;

public interface IThreadDeletionService
{
    /// Permanently erases a thread's real SMS/MMS messages from the device. Irreversible — this
    /// is the one operation in this app that does not go through Trash/Undo, since it destroys
    /// the actual messages rather than an app-local overlay.
    Task DeleteThreadAsync(long threadId);
}
