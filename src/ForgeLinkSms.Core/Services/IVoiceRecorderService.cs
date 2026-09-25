using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IVoiceRecorderService
{
    /// Asks for microphone permission if needed; false when it's denied or recording can't start.
    Task<bool> StartAsync();

    /// Stops and returns the recording as an attachment ready to send, or null if nothing usable was recorded.
    Task<PickedAttachment?> StopAsync();

    void Cancel();
}
