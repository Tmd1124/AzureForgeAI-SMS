namespace SmsMessenger.Core.Models;

public class UserProfile
{
    public required string DisplayName { get; init; }
    public string? PhotoPath { get; init; }
}
