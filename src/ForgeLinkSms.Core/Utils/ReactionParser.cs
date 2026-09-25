using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Utils;

public sealed record ParsedReaction(string Emoji, string? QuotedText, bool TargetsImage, bool IsRemoval);

// Recognizes the plain-text messages phones send for a reaction when the other side can't
// receive a real one: iPhone tapbacks ("Loved “…”"), newer custom ones ("Reacted 🎉 to “…”"),
// their removals, and ForgeLink's own ("👍 to "…"").
public static partial class ReactionParser
{
    private static readonly Dictionary<string, string> TapbackEmoji = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Loved"] = "❤️",
        ["Liked"] = "👍",
        ["Disliked"] = "👎",
        ["Laughed at"] = "😂",
        ["Emphasized"] = "‼️",
        ["Questioned"] = "❓"
    };

    private static readonly Dictionary<string, string> RemovedEmoji = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a heart"] = "❤️",
        ["a like"] = "👍",
        ["a dislike"] = "👎",
        ["a laugh"] = "😂",
        ["an exclamation"] = "‼️",
        ["a question mark"] = "❓"
    };

    private const string Quoted = @"[“""](?<quote>.+)[”""]";
    private const string Target = @"(?:" + Quoted + @"|(?<image>an image))";

    [GeneratedRegex(@"^(?<verb>Loved|Liked|Disliked|Laughed at|Emphasized|Questioned) " + Target + "$")]
    private static partial Regex TapbackRegex();

    [GeneratedRegex(@"^Reacted (?<emoji>\S+) to " + Target + "$")]
    private static partial Regex CustomRegex();

    [GeneratedRegex(@"^Removed (?<what>a heart|a like|a dislike|a laugh|an exclamation|a question mark|\S+) from " + Target + "$")]
    private static partial Regex RemovedRegex();

    [GeneratedRegex(@"^(?<emoji>[^\p{L}\p{N}\s]+) to ""(?<quote>.+)""$")]
    private static partial Regex ForgeLinkRegex();

    public static ParsedReaction? Parse(string body)
    {
        var text = body.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (TapbackRegex().Match(text) is { Success: true } tapback)
        {
            return Build(TapbackEmoji[tapback.Groups["verb"].Value], tapback, isRemoval: false);
        }
        if (CustomRegex().Match(text) is { Success: true } custom)
        {
            return Build(custom.Groups["emoji"].Value, custom, isRemoval: false);
        }
        if (RemovedRegex().Match(text) is { Success: true } removed)
        {
            var what = removed.Groups["what"].Value;
            return Build(RemovedEmoji.TryGetValue(what, out var emoji) ? emoji : what, removed, isRemoval: true);
        }
        if (ForgeLinkRegex().Match(text) is { Success: true } own)
        {
            return Build(own.Groups["emoji"].Value, own, isRemoval: false);
        }
        return null;
    }

    private static ParsedReaction Build(string emoji, Match match, bool isRemoval) =>
        match.Groups["image"].Success
            ? new ParsedReaction(emoji, null, TargetsImage: true, isRemoval)
            : new ParsedReaction(emoji, match.Groups["quote"].Value, TargetsImage: false, isRemoval);
}
