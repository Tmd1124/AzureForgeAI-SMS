using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using AndroidMediaRecorder = global::Android.Media.MediaRecorder;
using AndroidAudioSource = global::Android.Media.AudioSource;
using AndroidOutputFormat = global::Android.Media.OutputFormat;
using AndroidAudioEncoder = global::Android.Media.AudioEncoder;

namespace ForgeLinkSms.Platforms.Android;

// Records AMR-NB: the codec phones and carriers universally accept for voice messages sent as
// MMS, and small enough (~1.6KB/s) to stay under carrier size limits.
public class VoiceRecorderService : IVoiceRecorderService
{
    private AndroidMediaRecorder? _recorder;
    private string? _path;
    private DateTime _startedAtUtc;

    public async Task<bool> StartAsync()
    {
        var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Microphone>());
        if (status != PermissionStatus.Granted)
        {
            return false;
        }

        Cancel();
        var directory = Path.Combine(FileSystem.AppDataDirectory, "attachments");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, $"voice-message-{DateTime.Now:yyyyMMdd-HHmmss}.amr");

        try
        {
            _recorder = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? new AndroidMediaRecorder(Platform.AppContext)
                : new AndroidMediaRecorder();
            _recorder.SetAudioSource(AndroidAudioSource.Mic);
            _recorder.SetOutputFormat(AndroidOutputFormat.AmrNb);
            _recorder.SetAudioEncoder(AndroidAudioEncoder.AmrNb);
            _recorder.SetOutputFile(_path);
            _recorder.Prepare();
            _recorder.Start();
            _startedAtUtc = DateTime.UtcNow;
            return true;
        }
        catch (Exception)
        {
            Cancel();
            return false;
        }
    }

    public Task<PickedAttachment?> StopAsync()
    {
        var recorder = _recorder;
        var path = _path;
        _recorder = null;
        _path = null;
        if (recorder is null || path is null)
        {
            return Task.FromResult<PickedAttachment?>(null);
        }

        var duration = DateTime.UtcNow - _startedAtUtc;
        try
        {
            recorder.Stop();
        }
        catch (Java.Lang.RuntimeException)
        {
            // MediaRecorder throws when stopped before it captured any audio.
            duration = TimeSpan.Zero;
        }
        recorder.Release();

        if (!VoiceNote.IsWorthSending(duration) || !File.Exists(path))
        {
            TryDelete(path);
            return Task.FromResult<PickedAttachment?>(null);
        }
        return Task.FromResult<PickedAttachment?>(new PickedAttachment { FileName = Path.GetFileName(path), LocalPath = path, Kind = AttachmentKind.Audio });
    }

    public void Cancel()
    {
        var recorder = _recorder;
        var path = _path;
        _recorder = null;
        _path = null;
        if (recorder is not null)
        {
            try
            {
                recorder.Stop();
            }
            catch (Java.Lang.RuntimeException)
            {
            }
            recorder.Release();
        }
        if (path is not null)
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
