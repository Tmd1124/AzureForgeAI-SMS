namespace ForgeLinkSms.Core.Models;

public class ContactInfo
{
    public required string DisplayName { get; init; }
    public required string PhoneNumber { get; init; }
    public string? PhotoUri { get; init; }

    public string Initials
    {
        get
        {
            var words = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var letters = words.Take(2).Select(FirstAlphanumericChar).Where(c => c is not null).Select(c => c!.Value);
            return string.Concat(letters).ToUpperInvariant();
        }
    }

    private static char? FirstAlphanumericChar(string word)
    {
        foreach (var c in word)
        {
            if (char.IsLetterOrDigit(c))
            {
                return c;
            }
        }
        return null;
    }
}
