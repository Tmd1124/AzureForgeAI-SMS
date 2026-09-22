using SQLite;

namespace ForgeLinkSms.Core.Models;

public class Filter
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;
}
