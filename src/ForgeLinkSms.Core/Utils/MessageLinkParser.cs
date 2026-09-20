using System.Globalization;
using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Utils;

public enum MessageLinkKind
{
    PlainText,
    Url,
    PhoneNumber,
    Address,
    Appointment
}

public sealed record MessageTextSegment(string Text, MessageLinkKind Kind, object? Data = null);

public static partial class MessageLinkParser
{
    private const string StreetSuffixes =
        "St|Street|Ave|Avenue|Rd|Road|Blvd|Boulevard|Dr|Drive|Ln|Lane|Way|Ct|Court|Pl|Place|Cir|Circle|Ter|Terrace|Pkwy|Parkway";

    private const string Weekdays =
        "Mon(?:day)?|Tue(?:s(?:day)?)?|Wed(?:nesday)?|Thu(?:rs(?:day)?)?|Fri(?:day)?|Sat(?:urday)?|Sun(?:day)?";

    private const string Months =
        "January|February|March|April|May|June|July|August|September|October|November|December|" +
        "Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec";

    private const string Time = @"\d{1,2}(?::\d{2})?\s*(?:am|pm|AM|PM)?";

    // Alternation order doubles as detector priority: a match earlier in this
    // list wins over one later in it when they'd otherwise overlap.
    [GeneratedRegex(
        $@"(?<url>\bhttps?://[^\s]+\b|\bwww\.[^\s]+\b)" +
        $@"|(?<phone>(?:\+?1[-.\s]?)?\(?\d{{3}}\)?[-.\s]\d{{3}}[-.\s]\d{{4}}\b)" +
        $@"|(?<appt>\b(?:(?:{Weekdays})|today|tonight|tomorrow)\b\s*(?:at\s*)?{Time}\b" +
        $@"|\b\d{{1,2}}/\d{{1,2}}(?:/\d{{2,4}})?\s*(?:at\s*)?{Time}\b" +
        $@"|\b(?:{Months})\s+\d{{1,2}}(?:st|nd|rd|th)?(?:,?\s+\d{{4}})?\s*(?:at\s*)?{Time}\b)" +
        $@"|(?<address>\b\d{{1,6}}\s+(?:[A-Z][a-zA-Z]*\s){{1,4}}(?:{StreetSuffixes})\b\.?(?:,\s*[A-Za-z][A-Za-z\s]*)?(?:,\s*[A-Z]{{2}})?(?:\s+\d{{5}}(?:-\d{{4}})?)?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex EntityRegex();

    public static IReadOnlyList<MessageTextSegment> Parse(string text, DateTime? now = null)
    {
        var reference = now ?? DateTime.Now;
        var segments = new List<MessageTextSegment>();
        var lastIndex = 0;

        foreach (Match match in EntityRegex().Matches(text))
        {
            if (match.Index > lastIndex)
            {
                segments.Add(new MessageTextSegment(text[lastIndex..match.Index], MessageLinkKind.PlainText));
            }

            if (match.Groups["url"].Success)
            {
                segments.Add(new MessageTextSegment(match.Value, MessageLinkKind.Url, BuildUrl(match.Value)));
            }
            else if (match.Groups["phone"].Success)
            {
                segments.Add(new MessageTextSegment(match.Value, MessageLinkKind.PhoneNumber, PhoneNumberFormatter.ToComparableDigits(match.Value)));
            }
            else if (match.Groups["appt"].Success)
            {
                var parsed = TryParseAppointment(match.Value, reference);
                if (parsed is null)
                {
                    segments.Add(new MessageTextSegment(match.Value, MessageLinkKind.PlainText));
                }
                else
                {
                    segments.Add(new MessageTextSegment(match.Value, MessageLinkKind.Appointment, parsed.Value));
                }
            }
            else if (match.Groups["address"].Success)
            {
                segments.Add(new MessageTextSegment(match.Value, MessageLinkKind.Address, match.Value));
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            segments.Add(new MessageTextSegment(text[lastIndex..], MessageLinkKind.PlainText));
        }

        if (segments.Count == 0)
        {
            segments.Add(new MessageTextSegment(text, MessageLinkKind.PlainText));
        }

        return segments;
    }

    private static string BuildUrl(string raw) => raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? raw : "https://" + raw;

    // Matched first: an explicit meridiem removes all ambiguity, so this
    // takes priority over any bare number elsewhere in the phrase (e.g. the
    // "9" in "9/25 at 10am" must not be mistaken for the time).
    [GeneratedRegex(@"(\d{1,2})(?::(\d{2}))?\s*(am|pm)", RegexOptions.IgnoreCase)]
    private static partial Regex TimeWithMeridiemRegex();

    // Fallback for a bare hour with no am/pm, only trusted right after "at"
    // (e.g. "tonight at 7").
    [GeneratedRegex(@"\bat\s+(\d{1,2})(?::(\d{2}))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimeAfterAtRegex();

    private static DateTime? TryParseAppointment(string text, DateTime reference)
    {
        var timeMatch = TimeWithMeridiemRegex().Match(text);
        var hasMeridiem = timeMatch.Success;
        if (!hasMeridiem)
        {
            timeMatch = TimeAfterAtRegex().Match(text);
        }

        if (!timeMatch.Success)
        {
            return null;
        }

        var hour = int.Parse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = timeMatch.Groups[2].Success ? int.Parse(timeMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        var meridiem = hasMeridiem ? timeMatch.Groups[3].Value.ToLowerInvariant() : null;

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return null;
        }

        if (meridiem == "pm" && hour < 12)
        {
            hour += 12;
        }
        else if (meridiem == "am" && hour == 12)
        {
            hour = 0;
        }
        else if (meridiem is null && hour is >= 1 and <= 11)
        {
            // No explicit am/pm: "tonight" always means evening; otherwise
            // default afternoon-ish hours to PM, morning-ish hours to AM
            // (matches the common shorthand "call at 7" == 7pm).
            var isEvening = text.Contains("tonight", StringComparison.OrdinalIgnoreCase) || hour is >= 1 and <= 6;
            hour = isEvening ? hour + 12 : hour;
        }

        var datePart = ResolveDate(text, reference);
        if (datePart is null)
        {
            return null;
        }

        return datePart.Value.Date.AddHours(hour).AddMinutes(minute);
    }

    private static DateTime? ResolveDate(string text, DateTime reference)
    {
        if (text.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            return reference.Date.AddDays(1);
        }

        if (text.Contains("tonight", StringComparison.OrdinalIgnoreCase) || text.Contains("today", StringComparison.OrdinalIgnoreCase))
        {
            return reference.Date;
        }

        var weekdayMatch = Regex.Match(text, $@"\b({Weekdays})\b", RegexOptions.IgnoreCase);
        if (weekdayMatch.Success)
        {
            return NextWeekday(reference.Date, weekdayMatch.Value);
        }

        var numericMatch = Regex.Match(text, @"\b(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?\b");
        if (numericMatch.Success)
        {
            var month = int.Parse(numericMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(numericMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var year = numericMatch.Groups[3].Success
                ? NormalizeYear(numericMatch.Groups[3].Value)
                : reference.Year;
            return new DateTime(year, month, day);
        }

        var monthNameMatch = Regex.Match(text, $@"\b({Months})\s+(\d{{1,2}})(?:st|nd|rd|th)?(?:,?\s+(\d{{4}}))?\b", RegexOptions.IgnoreCase);
        if (monthNameMatch.Success)
        {
            var month = DateTime.ParseExact(monthNameMatch.Groups[1].Value, monthNameMatch.Groups[1].Value.Length > 3 ? "MMMM" : "MMM", CultureInfo.InvariantCulture).Month;
            var day = int.Parse(monthNameMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var year = monthNameMatch.Groups[3].Success ? int.Parse(monthNameMatch.Groups[3].Value, CultureInfo.InvariantCulture) : reference.Year;
            return new DateTime(year, month, day);
        }

        return null;
    }

    private static int NormalizeYear(string raw)
    {
        var year = int.Parse(raw, CultureInfo.InvariantCulture);
        return year < 100 ? 2000 + year : year;
    }

    private static DateTime NextWeekday(DateTime fromDate, string weekdayText)
    {
        var target = weekdayText[..3].ToLowerInvariant() switch
        {
            "mon" => DayOfWeek.Monday,
            "tue" => DayOfWeek.Tuesday,
            "wed" => DayOfWeek.Wednesday,
            "thu" => DayOfWeek.Thursday,
            "fri" => DayOfWeek.Friday,
            "sat" => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday
        };

        var daysAhead = ((int)target - (int)fromDate.DayOfWeek + 7) % 7;
        daysAhead = daysAhead == 0 ? 7 : daysAhead;
        return fromDate.AddDays(daysAhead);
    }
}
