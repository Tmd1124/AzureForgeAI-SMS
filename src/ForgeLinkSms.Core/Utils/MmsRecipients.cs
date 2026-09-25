namespace ForgeLinkSms.Core.Utils;

public static class MmsRecipients
{
    // Everyone on a message except the user: this set is what identifies the conversation
    // (Android's Threads.getOrCreateThreadId), so including the user's own number would file a
    // group message into a brand-new thread instead of the existing group.
    public static IReadOnlyList<string> OtherParticipants(string? from, IReadOnlyList<string> to, IReadOnlyList<string> cc, IReadOnlyList<string> selfNumbers)
    {
        var selfKeys = selfNumbers.Select(Key).Where(k => k.Length > 0).ToHashSet();

        // With the user's own number unknown, a message with exactly one recipient can only have
        // been addressed to the user, so the sender alone is the other participant.
        if (selfKeys.Count == 0 && !string.IsNullOrWhiteSpace(from) && to.Count + cc.Count == 1)
        {
            return new[] { from };
        }

        var seen = new HashSet<string>();
        var others = new List<string>();
        foreach (var address in new[] { from }.Concat(to).Concat(cc))
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }
            var key = Key(address);
            if (selfKeys.Contains(key) || !seen.Add(key))
            {
                continue;
            }
            others.Add(address);
        }
        return others;
    }

    // The user's own number is whichever address shows up as a recipient on the most incoming
    // messages; used when the carrier doesn't expose it through TelephonyManager.
    public static string? MostLikelySelfNumber(IEnumerable<IReadOnlyList<string>> incomingRecipients) =>
        incomingRecipients
            .SelectMany(r => r)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(Key)
            .OrderByDescending(g => g.Count())
            .Select(g => g.First())
            .FirstOrDefault();

    private static string Key(string address)
    {
        var digits = PhoneNumberFormatter.ToComparableDigits(address);
        return digits.Length > 0 ? digits : address.Trim().ToLowerInvariant();
    }
}
