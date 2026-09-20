namespace ForgeLinkSms.Core.Data;

public interface IFavoriteRepository
{
    Task InitializeAsync();
    Task FavoriteThreadAsync(long threadId);
    Task UnfavoriteThreadAsync(long threadId);
    Task<bool> IsFavoriteAsync(long threadId);
    Task<IReadOnlyList<long>> GetFavoriteThreadIdsAsync();
}
