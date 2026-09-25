namespace ForgeLinkSms.Core.Services;

public interface IAudioPlaybackService
{
    /// Plays an MMS audio part (content://mms/part/{partId}); starting another part stops the current one.
    void Play(long partId);

    void Stop();

    /// The part currently playing, or null.
    long? PlayingPartId { get; }

    /// Raised on a background thread whenever playback starts, stops, or finishes.
    event Action? PlaybackChanged;
}
