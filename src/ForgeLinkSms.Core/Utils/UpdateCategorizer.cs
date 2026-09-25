using System.Text.RegularExpressions;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static partial class UpdateCategorizer
{
    [GeneratedRegex(@"\b(appointment|appt|reschedul\w*|check[- ]?in|doctor|dr\.|dental|dentist|clinic|pharmacy|prescription|rx|refill|reservation|booking|visit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AppointmentRegex();

    [GeneratedRegex(@"\b(deliver\w*|package|parcel|shipment|shipped|ships|tracking|out for delivery|ups|fedex|usps|dhl|amazon|order)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeliveryRegex();

    [GeneratedRegex(@"\b(bank|banking|account|card|debit|credit|balance|transaction|purchase|payment|paid|deposit\w*|withdraw\w*|transfer|zelle|venmo|paypal|fraud|chase|wells fargo|citi|capital one)\b|\$\d", RegexOptions.IgnoreCase)]
    private static partial Regex BankingRegex();

    [GeneratedRegex(@"\b(sale|offer|deal|discount|promo\w*|coupon|save|free|win|winner|limited time|exclusive|coverage|insurance|quote|interested in|opt[- ]?out|unsubscribe|reply stop|stop to)\b|\d+% off", RegexOptions.IgnoreCase)]
    private static partial Regex PromotionRegex();

    // Order matters: "Your pharmacy order is ready" is an appointment-type reminder, and a
    // delivery text often mentions a $ amount, so the narrower categories are checked first.
    public static UpdateCategory Categorize(string body, string senderName)
    {
        if (OneTimeCodeDetector.Extract(body) is not null)
        {
            return UpdateCategory.Codes;
        }

        var text = $"{senderName} {body}";
        if (AppointmentRegex().IsMatch(text))
        {
            return UpdateCategory.Appointments;
        }
        if (DeliveryRegex().IsMatch(text))
        {
            return UpdateCategory.Deliveries;
        }
        if (BankingRegex().IsMatch(text))
        {
            return UpdateCategory.Banking;
        }
        if (PromotionRegex().IsMatch(text))
        {
            return UpdateCategory.Promotions;
        }
        return UpdateCategory.Other;
    }
}
