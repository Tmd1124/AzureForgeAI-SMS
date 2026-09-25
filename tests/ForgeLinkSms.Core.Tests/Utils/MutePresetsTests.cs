using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class MutePresetsTests
{
    [Fact]
    public void Offers_an_hour_eight_hours_a_day_and_always()
    {
        var now = new DateTimeOffset(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);

        var presets = MutePresets.For(now);

        Assert.Equal(new[] { "1 hour", "8 hours", "1 day", "Always" }, presets.Select(p => p.Label));
        Assert.Equal(new DateTimeOffset?[] { now.AddHours(1), now.AddHours(8), now.AddDays(1), null }, presets.Select(p => p.UntilUtc));
    }
}
