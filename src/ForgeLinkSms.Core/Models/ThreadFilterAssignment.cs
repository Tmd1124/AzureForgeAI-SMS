using SQLite;

namespace ForgeLinkSms.Core.Models;

public class ThreadFilterAssignment
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public long ThreadId { get; set; }

    public long FilterId { get; set; }
}
