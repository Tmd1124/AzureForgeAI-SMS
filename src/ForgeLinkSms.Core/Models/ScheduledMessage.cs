using SQLite;

namespace ForgeLinkSms.Core.Models;

public class ScheduledMessage
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Address { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset SendAtUtc { get; set; }
}
