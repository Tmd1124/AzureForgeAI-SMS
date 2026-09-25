using SQLite;

namespace ForgeLinkSms.Core.Models;

public class Draft
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
