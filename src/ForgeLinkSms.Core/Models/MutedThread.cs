using SQLite;

namespace ForgeLinkSms.Core.Models;

public class MutedThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    /// Null means muted until the user unmutes it.
    public DateTimeOffset? UntilUtc { get; set; }
}
