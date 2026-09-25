using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.Tests.Web;

public class PairingServiceTests
{
    private sealed class MemoryStore : IPairedBrowserStore
    {
        private readonly HashSet<string> _hashes = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task AddAsync(string tokenHash) { _hashes.Add(tokenHash); return Task.CompletedTask; }
        public Task<bool> ContainsAsync(string tokenHash) => Task.FromResult(_hashes.Contains(tokenHash));
        public Task<int> CountAsync() => Task.FromResult(_hashes.Count);
        public Task ClearAsync() { _hashes.Clear(); return Task.CompletedTask; }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly MemoryStore _store = new();
    private readonly TestClock _clock = new();

    private PairingService MakeService() => new(_store, _clock);

    [Fact]
    public void The_code_is_six_digits()
    {
        Assert.Matches("^[0-9]{6}$", MakeService().Code);
    }

    [Fact]
    public async Task The_right_code_pairs_and_the_token_authorizes()
    {
        var service = MakeService();

        var result = await service.PairAsync(service.Code, "192.168.1.130");

        Assert.Equal(PairingOutcome.Paired, result.Outcome);
        Assert.True(await service.IsAuthorizedAsync(result.Token));
        Assert.False(await service.IsAuthorizedAsync("made-up-token"));
        Assert.False(await service.IsAuthorizedAsync(null));
    }

    [Fact]
    public async Task Pairing_changes_the_code_so_it_cannot_be_reused()
    {
        var service = MakeService();
        var code = service.Code;

        await service.PairAsync(code, "192.168.1.130");

        Assert.NotEqual(code, service.Code);
    }

    [Fact]
    public async Task A_wrong_code_is_rejected()
    {
        var service = MakeService();

        var result = await service.PairAsync(service.Code == "000000" ? "111111" : "000000", "192.168.1.130");

        Assert.Equal(PairingOutcome.WrongCode, result.Outcome);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_out_even_the_right_one()
    {
        var service = MakeService();
        var wrong = service.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await service.PairAsync(wrong, "192.168.1.130");
        }

        var result = await service.PairAsync(service.Code, "192.168.1.130");

        Assert.Equal(PairingOutcome.LockedOut, result.Outcome);
    }

    [Fact]
    public async Task The_lockout_ends_after_a_minute()
    {
        var service = MakeService();
        var wrong = service.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await service.PairAsync(wrong, "192.168.1.130");
        }

        _clock.Now = _clock.Now.AddSeconds(61);

        Assert.Equal(PairingOutcome.Paired, (await service.PairAsync(service.Code, "192.168.1.130")).Outcome);
    }

    [Fact]
    public async Task Lockout_is_per_browser()
    {
        var service = MakeService();
        var wrong = service.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await service.PairAsync(wrong, "192.168.1.130");
        }

        Assert.Equal(PairingOutcome.Paired, (await service.PairAsync(service.Code, "192.168.1.44")).Outcome);
    }

    [Fact]
    public async Task Guessing_from_many_addresses_changes_the_code_and_pauses_pairing()
    {
        var service = MakeService();
        var original = service.Code;
        var wrong = original == "000000" ? "111111" : "000000";
        for (var i = 0; i < PairingService.MaxWrongCodesOverall; i++)
        {
            await service.PairAsync(wrong, $"127.0.0.{i + 2}");
        }

        Assert.NotEqual(original, service.Code);
        Assert.Equal(PairingOutcome.LockedOut, (await service.PairAsync(service.Code, "192.168.1.44")).Outcome);
        _clock.Now = _clock.Now.AddSeconds(61);
        Assert.Equal(PairingOutcome.Paired, (await service.PairAsync(service.Code, "192.168.1.44")).Outcome);
    }

    [Fact]
    public async Task UnpairAll_revokes_every_browser()
    {
        var service = MakeService();
        var first = (await service.PairAsync(service.Code, "192.168.1.130")).Token;
        var second = (await service.PairAsync(service.Code, "192.168.1.44")).Token;
        Assert.Equal(2, await service.PairedCountAsync());

        await service.UnpairAllAsync();

        Assert.False(await service.IsAuthorizedAsync(first));
        Assert.False(await service.IsAuthorizedAsync(second));
        Assert.Equal(0, await service.PairedCountAsync());
    }

    [Fact]
    public async Task A_paired_browser_stays_paired_after_the_app_restarts()
    {
        var before = MakeService();
        var token = (await before.PairAsync(before.Code, "192.168.1.130")).Token;

        var after = MakeService();

        Assert.True(await after.IsAuthorizedAsync(token));
    }

    [Fact]
    public async Task PairedBrowserStore_persists_hashes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"paired-{Guid.NewGuid()}.db3");
        using (var store = new PairedBrowserStore(path))
        {
            await store.InitializeAsync();
            await store.AddAsync("abc");
            Assert.True(await store.ContainsAsync("abc"));
            Assert.Equal(1, await store.CountAsync());
            await store.ClearAsync();
            Assert.False(await store.ContainsAsync("abc"));
        }
        File.Delete(path);
    }
}
