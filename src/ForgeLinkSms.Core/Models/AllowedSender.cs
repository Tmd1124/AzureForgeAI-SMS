using SQLite;

namespace ForgeLinkSms.Core.Models;

public class AllowedSender
{
    [PrimaryKey]
    public string NormalizedAddress { get; set; } = string.Empty;

    public DateTimeOffset AllowedAtUtc { get; set; }
}
