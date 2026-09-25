using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class ReactionAttacher
{
    // Turns reaction texts into badges on the message they quote and hides the reaction text.
    // Recomputed from scratch each time so it can run again whenever more history loads.
    // Expects messages oldest first; a reaction whose message isn't loaded stays visible as text.
    public static void Apply(IList<SmsMessage> messages)
    {
        foreach (var message in messages)
        {
            message.Reactions.Clear();
            message.IsHiddenReaction = false;
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.Attachments.Count > 0 || ReactionParser.Parse(message.Body) is not { } reaction)
            {
                continue;
            }

            var target = FindTarget(messages, i, reaction);
            if (target is null)
            {
                continue;
            }

            if (reaction.IsRemoval)
            {
                target.Reactions.Remove(reaction.Emoji);
            }
            else
            {
                target.Reactions.Add(reaction.Emoji);
            }
            message.IsHiddenReaction = true;
        }
    }

    private static SmsMessage? FindTarget(IList<SmsMessage> messages, int reactionIndex, ParsedReaction reaction)
    {
        for (var j = reactionIndex - 1; j >= 0; j--)
        {
            var candidate = messages[j];
            if (candidate.IsHiddenReaction)
            {
                continue;
            }
            var matches = reaction.TargetsImage
                ? candidate.Attachments.Any(a => a.Kind is AttachmentKind.Image or AttachmentKind.Gif)
                : QuoteMatches(candidate.Body, reaction.QuotedText!);
            if (matches)
            {
                return candidate;
            }
        }
        return null;
    }

    // Phones shorten long quotes with "…", so a shortened quote matches by its beginning.
    private static bool QuoteMatches(string body, string quoted)
    {
        var text = body.Trim();
        var quote = quoted.Trim();
        if (quote.EndsWith('…') || quote.EndsWith("..."))
        {
            var prefix = quote.TrimEnd('…', '.').TrimEnd();
            return prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal);
        }
        return text == quote;
    }
}
