using SQLite;

namespace SmsMessenger.Core.Models;

public class BlockedNumber
{
    [PrimaryKey]
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset BlockedAtUtc { get; set; }
}
