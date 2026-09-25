using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class PondLayoutTests
{
    // Pond widths in CSS px: a 360px phone, the Fold's cover screen, and its inner screen
    // (page gutters already subtracted).
    public static TheoryData<int> Widths => new() { 336, 387, 676 };

    // Bubbles bob up to 5px upward (never down) and the unread badge overhangs 4px at the top
    // right; the name moves with its bubble.
    private const double Bob = 5;
    private const double Badge = 4;
    private const double NameWidth = 76;
    private const double NameHeight = 18;
    private const double NameGap = 3;

    private sealed record Box(string Label, double Left, double Top, double Right, double Bottom);

    private static List<Box> Boxes(int width)
    {
        var boxes = new List<Box>();
        for (var i = 0; i < PondLayout.Slots.Count; i++)
        {
            var (xPercent, y) = PondLayout.Slots[i];
            var size = PondLayout.Sizes[i];
            var cx = width * xPercent / 100.0;
            boxes.Add(new Box($"bubble{i}", cx - size / 2.0, y - size / 2.0 - Bob - Badge, cx + size / 2.0 + Badge, y + size / 2.0));
            var nameTop = y + size / 2.0 + NameGap;
            boxes.Add(new Box($"name{i}", cx - NameWidth / 2, nameTop - Bob, cx + NameWidth / 2, nameTop + NameHeight));
        }
        return boxes;
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void No_bubble_or_name_overlaps_another_bubble_or_name(int width)
    {
        var boxes = Boxes(width);
        var collisions = boxes
            .SelectMany((a, i) => boxes.Skip(i + 1).Select(b => (a, b)))
            .Where(p => p.a.Label[^1] != p.b.Label[^1])
            .Where(p => Math.Min(p.a.Right, p.b.Right) > Math.Max(p.a.Left, p.b.Left)
                     && Math.Min(p.a.Bottom, p.b.Bottom) > Math.Max(p.a.Top, p.b.Top))
            .Select(p => $"{p.a.Label}/{p.b.Label}")
            .ToList();

        Assert.Empty(collisions);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void Everything_stays_inside_the_pond(int width)
    {
        var outside = Boxes(width)
            .Where(b => b.Left < -2 || b.Right > width + 2 || b.Top < 0 || b.Bottom > PondLayout.Height)
            .Select(b => b.Label)
            .ToList();

        Assert.Empty(outside);
    }

    [Fact]
    public void There_is_a_slot_and_size_for_every_pond_bubble()
    {
        Assert.Equal(PondSelector.MaxBubbles, PondLayout.Slots.Count);
        Assert.Equal(PondSelector.MaxBubbles, PondLayout.Sizes.Count);
    }
}
