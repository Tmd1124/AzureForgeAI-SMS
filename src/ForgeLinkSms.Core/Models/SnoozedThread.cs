using SQLite;

namespace ForgeLinkSms.Core.Models;

public class SnoozedThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public DateTimeOffset UntilUtc { get; set; }
}
