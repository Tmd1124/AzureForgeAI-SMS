using System.Globalization;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class AttachmentTextFormatter
{
    public static string FormatLocationLink(double latitude, double longitude) =>
        $"https://maps.google.com/?q={latitude.ToString(CultureInfo.InvariantCulture)},{longitude.ToString(CultureInfo.InvariantCulture)}";

    public static string FormatContactShare(ContactInfo contact)
    {
        var digits = PhoneNumberFormatter.ToComparableDigits(contact.PhoneNumber);
        var tel = digits.Length == 10 ? $"+1{digits}" : $"+{digits}";
        return $"BEGIN:VCARD\nVERSION:3.0\nFN:{contact.DisplayName}\nTEL:{tel}\nEND:VCARD";
    }
}
