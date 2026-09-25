using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class SnoozePresetsTests
{
    [Fact]
    public void For_a_weekday_morning_offers_every_preset()
    {
        var wednesday9am = new DateTime(2026, 9, 23, 9, 0, 0);

        var presets = SnoozePresets.For(wednesday9am);

        Assert.Equal(new[] { "Later today", "This evening", "Tomorrow morning", "This weekend", "Next week" }, presets.Select(p => p.Label));
        Assert.Equal(new DateTime(2026, 9, 23, 12, 0, 0), presets[0].Until);
        Assert.Equal(new DateTime(2026, 9, 23, 18, 0, 0), presets[1].Until);
        Assert.Equal(new DateTime(2026, 9, 24, 8, 0, 0), presets[2].Until);
        Assert.Equal(new DateTime(2026, 9, 26, 8, 0, 0), presets[3].Until);
        Assert.Equal(new DateTime(2026, 9, 28, 8, 0, 0), presets[4].Until);
    }

    [Fact]
    public void For_a_late_evening_drops_this_evening()
    {
        var wednesday8pm = new DateTime(2026, 9, 23, 20, 0, 0);

        Assert.DoesNotContain(SnoozePresets.For(wednesday8pm), p => p.Label == "This evening");
    }

    [Theory]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    public void For_friday_through_sunday_drops_this_weekend(int day)
    {
        var presets = SnoozePresets.For(new DateTime(2026, 9, day, 9, 0, 0));

        Assert.DoesNotContain(presets, p => p.Label == "This weekend");
    }

    [Fact]
    public void Next_week_on_a_monday_is_the_following_monday()
    {
        var monday = new DateTime(2026, 9, 21, 9, 0, 0);

        Assert.Equal(new DateTime(2026, 9, 28, 8, 0, 0), SnoozePresets.For(monday).Single(p => p.Label == "Next week").Until);
    }
}
