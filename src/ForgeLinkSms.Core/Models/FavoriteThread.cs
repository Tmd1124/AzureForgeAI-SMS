using SQLite;

namespace ForgeLinkSms.Core.Models;

public class FavoriteThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public DateTimeOffset FavoritedAtUtc { get; set; }
}
