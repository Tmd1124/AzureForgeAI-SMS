namespace SmsMessenger.Core.Models;

public class ContactInfo
{
    public required string DisplayName { get; init; }
    public required string PhoneNumber { get; init; }
    public string? PhotoUri { get; init; }
}
