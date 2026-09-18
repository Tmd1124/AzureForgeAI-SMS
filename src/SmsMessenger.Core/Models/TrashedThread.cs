using SQLite;

namespace SmsMessenger.Core.Models;

public class TrashedThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public DateTimeOffset TrashedAtUtc { get; set; }
}
