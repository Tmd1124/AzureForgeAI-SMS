using SQLite;

namespace SmsMessenger.Core.Models;

public class ArchivedThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public DateTimeOffset ArchivedAtUtc { get; set; }
}
