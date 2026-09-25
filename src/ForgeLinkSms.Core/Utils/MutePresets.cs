namespace ForgeLinkSms.Core.Utils;

public record MutePreset(string Label, DateTimeOffset? UntilUtc);

public static class MutePresets
{
    public static IReadOnlyList<MutePreset> For(DateTimeOffset now) => new[]
    {
        new MutePreset("1 hour", now.AddHours(1)),
        new MutePreset("8 hours", now.AddHours(8)),
        new MutePreset("1 day", now.AddDays(1)),
        new MutePreset("Always", null)
    };
}
