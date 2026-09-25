using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class VoiceNoteTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7, "0:07")]
    [InlineData(65, "1:05")]
    [InlineData(120, "2:00")]
    public void FormatDuration_shows_minutes_and_seconds(int seconds, string expected)
    {
        Assert.Equal(expected, VoiceNote.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Recordings_stop_at_two_minutes_to_stay_under_carrier_picture_message_limits()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), VoiceNote.MaxDuration);
    }

    [Theory]
    [InlineData(0.4, false)]
    [InlineData(1.0, true)]
    public void A_recording_shorter_than_a_second_is_treated_as_an_accidental_tap(double seconds, bool worthSending)
    {
        Assert.Equal(worthSending, VoiceNote.IsWorthSending(TimeSpan.FromSeconds(seconds)));
    }
}
