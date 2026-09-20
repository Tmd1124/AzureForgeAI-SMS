using SQLite;

namespace SmsMessenger.Core.Models;

public class FavoriteThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public DateTimeOffset FavoritedAtUtc { get; set; }
}
