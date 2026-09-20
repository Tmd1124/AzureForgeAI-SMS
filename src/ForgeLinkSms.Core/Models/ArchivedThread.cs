using SQLite;

namespace ForgeLinkSms.Core.Models;

public class ArchivedThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public DateTimeOffset ArchivedAtUtc { get; set; }
}
