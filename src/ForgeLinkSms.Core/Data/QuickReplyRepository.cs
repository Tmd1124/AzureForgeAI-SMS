using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class QuickReplyRepository : IQuickReplyRepository, IDisposable
{
    public static readonly IReadOnlyList<string> StarterReplies = new[]
    {
        "On my way!",
        "Running 10 minutes late",
        "Can't talk right now, I'll call you back",
        "Sounds good 👍",
        "Thanks!"
    };

    private readonly SQLiteAsyncConnection _db;

    public QuickReplyRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    // Records that the starter replies were added, separately from the replies themselves: a
    // user who deletes them never sees them come back, while a first launch that was cut off
    // before adding them still gets them next time.
    private class QuickReplySetup
    {
        [PrimaryKey]
        public int Id { get; set; }

        public bool StarterRepliesAdded { get; set; }
    }

    // ConfigureAwait(false) throughout: MauiProgram blocks Android's main thread on this during
    // startup, so a continuation waiting to resume there would deadlock the app (the same
    // failure FilterRepository.InitializeAsync once had).
    public async Task InitializeAsync()
    {
        await _db.CreateTableAsync<QuickReply>().ConfigureAwait(false);
        await _db.CreateTableAsync<QuickReplySetup>().ConfigureAwait(false);
        if (await _db.FindAsync<QuickReplySetup>(1).ConfigureAwait(false) is { StarterRepliesAdded: true })
        {
            return;
        }

        await _db.RunInTransactionAsync(connection =>
        {
            for (var i = 0; i < StarterReplies.Count; i++)
            {
                connection.Insert(new QuickReply { Text = StarterReplies[i], SortOrder = i });
            }
            connection.InsertOrReplace(new QuickReplySetup { Id = 1, StarterRepliesAdded = true });
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QuickReply>> GetAllAsync() =>
        await _db.Table<QuickReply>().OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync();

    public async Task AddAsync(string text)
    {
        var last = await _db.Table<QuickReply>().OrderByDescending(r => r.SortOrder).FirstOrDefaultAsync().ConfigureAwait(false);
        await _db.InsertAsync(new QuickReply { Text = text, SortOrder = (last?.SortOrder ?? -1) + 1 }).ConfigureAwait(false);
    }

    public async Task UpdateAsync(long id, string text)
    {
        if (await _db.FindAsync<QuickReply>(id).ConfigureAwait(false) is { } reply)
        {
            reply.Text = text;
            await _db.UpdateAsync(reply).ConfigureAwait(false);
        }
    }

    public Task DeleteAsync(long id) => _db.DeleteAsync<QuickReply>(id);

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
