namespace ForgeLinkSms.Core.Utils;

public record SnoozePreset(string Label, DateTime Until);

public static class SnoozePresets
{
    private const int MorningHour = 8;
    private const int EveningHour = 18;

    // Works in local wall-clock time because "tomorrow at 8 AM" means the user's own morning.
    public static IReadOnlyList<SnoozePreset> For(DateTime nowLocal)
    {
        var presets = new List<SnoozePreset> { new("Later today", nowLocal.AddHours(3)) };

        var tonight = nowLocal.Date.AddHours(EveningHour);
        if (nowLocal < tonight.AddHours(-1))
        {
            presets.Add(new SnoozePreset("This evening", tonight));
        }

        presets.Add(new SnoozePreset("Tomorrow morning", nowLocal.Date.AddDays(1).AddHours(MorningHour)));

        if (nowLocal.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Thursday)
        {
            var daysToSaturday = DayOfWeek.Saturday - nowLocal.DayOfWeek;
            presets.Add(new SnoozePreset("This weekend", nowLocal.Date.AddDays(daysToSaturday).AddHours(MorningHour)));
        }

        var daysToMonday = ((int)DayOfWeek.Monday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysToMonday == 0)
        {
            daysToMonday = 7;
        }
        presets.Add(new SnoozePreset("Next week", nowLocal.Date.AddDays(daysToMonday).AddHours(MorningHour)));

        return presets;
    }
}
