namespace ForgeLinkSms.Core.Utils;

public static class VoiceNote
{
    // AMR-NB records at ~1.6KB/s, so two minutes stays around 200KB — comfortably under the
    // 300KB–1MB picture-message limits carriers enforce.
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(1);

    public static bool IsWorthSending(TimeSpan duration) => duration >= MinDuration;

    public static string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
}
