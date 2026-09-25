using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class SenderScreening
{
    // A sender is "known" once they're a contact, the user has ever texted them, favorited the
    // conversation, or explicitly allowed them; everyone else waits in the Screener.
    public static bool IsScreened(SmsThread thread, IReadOnlySet<string> allowedAddresses) =>
        string.IsNullOrWhiteSpace(thread.DisplayName)
        && !thread.HasOutgoing
        && !thread.IsFavorite
        && !allowedAddresses.Contains(PhoneNumberFormatter.ToComparableDigits(thread.Address));

    // Screened texts arrive silently, except one-time codes: the user is usually waiting on
    // those right now, and they almost always come from numbers that aren't contacts.
    public static bool ShouldNotify(bool isContact, bool isAllowed, bool hasOutgoing, string body) =>
        isContact || isAllowed || hasOutgoing || OneTimeCodeDetector.Extract(body) is not null;
}
