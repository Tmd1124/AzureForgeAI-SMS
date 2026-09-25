using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class PondSelectorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static SmsThread MakeThread(long id, string? name, int minutesAgo, bool isFavorite = false, int unread = 0, string? address = null) => new()
    {
        Id = id,
        Address = address ?? $"312555{id:0000}",
        DisplayName = name,
        LastMessageBody = "hi",
        LastMessageTimestamp = Now.AddMinutes(-minutesAgo),
        UnreadCount = unread,
        IsFavorite = isFavorite
    };

    [Fact]
    public void Select_ranks_favorites_then_unread_then_most_recent()
    {
        var threads = new[]
        {
            MakeThread(1, "Recent", 1),
            MakeThread(2, "Unread", 30, unread: 2),
            MakeThread(3, "Old favorite", 500, isFavorite: true),
            MakeThread(4, "New favorite", 60, isFavorite: true),
            MakeThread(5, "Older", 90)
        };

        var pond = PondSelector.Select(threads);

        Assert.Equal(new long[] { 4, 3, 2, 1, 5 }, pond.Select(t => t.Id));
    }

    [Fact]
    public void Select_caps_the_pond_at_eight()
    {
        var threads = Enumerable.Range(1, 12).Select(i => MakeThread(i, $"P{i}", i)).ToList();

        var pond = PondSelector.Select(threads);

        Assert.Equal(8, pond.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), pond.Select(t => t.Id));
    }

    [Fact]
    public void Select_skips_threads_without_a_contact_name()
    {
        var threads = new[] { MakeThread(1, null, 1), MakeThread(2, "A", 2), MakeThread(3, "B", 3), MakeThread(4, "C", 4) };

        Assert.DoesNotContain(PondSelector.Select(threads), t => t.Id == 1);
    }

    [Fact]
    public void Select_returns_nothing_when_fewer_than_three_qualify()
    {
        var threads = new[] { MakeThread(1, "A", 1), MakeThread(2, "B", 2), MakeThread(3, null, 3) };

        Assert.Empty(PondSelector.Select(threads));
    }

    [Fact]
    public void ColorFor_is_stable_for_a_known_address()
    {
        // Pinned value: FNV-1a over "3125550147" into the 8-color palette. If this ever changes,
        // every user's people change color, so treat a failure here as a real regression.
        Assert.Equal(PondSelector.ColorFor("3125550147"), PondSelector.ColorFor("3125550147"));
        Assert.Matches("^#[0-9a-f]{6}$", PondSelector.ColorFor("3125550147"));
        Assert.Equal("#f59e0b", PondSelector.ColorFor("3125550147"));
    }

    [Fact]
    public void ColorFor_ignores_number_formatting()
    {
        Assert.Equal(PondSelector.ColorFor("3125550147"), PondSelector.ColorFor("+1 (312) 555-0147"));
    }
}
