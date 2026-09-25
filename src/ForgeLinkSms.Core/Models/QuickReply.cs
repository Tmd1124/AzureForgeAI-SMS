using SQLite;

namespace ForgeLinkSms.Core.Models;

public class QuickReply
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
