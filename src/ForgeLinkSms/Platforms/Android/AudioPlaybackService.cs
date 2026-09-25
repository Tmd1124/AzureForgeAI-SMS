using ForgeLinkSms.Core.Services;
using AndroidMediaPlayer = global::Android.Media.MediaPlayer;
using AndroidUri = global::Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

// Plays voice messages with Android's own MediaPlayer rather than an HTML <audio> element:
// the WebView can't decode AMR, the format nearly every phone sends voice messages in.
public class AudioPlaybackService : IAudioPlaybackService
{
    private readonly object _gate = new();
    private AndroidMediaPlayer? _player;

    public long? PlayingPartId { get; private set; }

    public event Action? PlaybackChanged;

    public void Play(long partId)
    {
        lock (_gate)
        {
            ReleasePlayer();
            try
            {
                var player = new AndroidMediaPlayer();
                player.SetDataSource(Platform.AppContext, AndroidUri.Parse($"content://mms/part/{partId}")!);
                player.Completion += (_, _) => Stop();
                player.Prepare();
                player.Start();
                _player = player;
                PlayingPartId = partId;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("ForgeLinkSms", $"Couldn't play audio part {partId}: {ex.Message}");
                ReleasePlayer();
            }
        }
        PlaybackChanged?.Invoke();
    }

    public void Stop()
    {
        lock (_gate)
        {
            ReleasePlayer();
        }
        PlaybackChanged?.Invoke();
    }

    private void ReleasePlayer()
    {
        _player?.Release();
        _player = null;
        PlayingPartId = null;
    }
}
