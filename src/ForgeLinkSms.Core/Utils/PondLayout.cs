namespace ForgeLinkSms.Core.Utils;

public static class PondLayout
{
    public const int Height = 260;

    // Bubble centers (x as % of pond width, y in px) in rank order; fewer than 8 bubbles use
    // the first N slots. Found by searching for positions where no bubble (with its bob and unread
    // badge) or name overlaps another at phone widths 336–676px — PondLayoutTests pins that.
    public static readonly IReadOnlyList<(double XPercent, int Y)> Slots = new (double, int)[]
    {
        (62, 53), (87, 156), (12, 46), (88, 43), (59, 158), (13, 141), (36, 40), (37, 120)
    };

    public static readonly IReadOnlyList<int> Sizes = new[] { 88, 80, 74, 68, 62, 56, 52, 48 };
}
