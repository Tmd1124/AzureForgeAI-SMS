using System.Security.Cryptography;
using System.Text;

namespace ForgeLinkSms.Core.Web;

public enum PairingOutcome
{
    Paired,
    WrongCode,
    LockedOut
}

public sealed record PairingResult(PairingOutcome Outcome, string? Token);

// Pairs a browser with the code shown on the phone. Browsers keep a random token (only its hash
// is stored), so pairing survives app restarts and can be revoked for everyone at once.
public class PairingService
{
    public const int MaxWrongCodes = 5;
    public const int MaxWrongCodesOverall = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly IPairedBrowserStore _store;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _wrongAttempts = new();
    private readonly Dictionary<string, DateTimeOffset> _lockedUntil = new();
    // Counted across all addresses too: one device can present many addresses (IP aliases).
    private readonly List<DateTimeOffset> _allWrongAttempts = new();
    private DateTimeOffset _pairingPausedUntil;

    public PairingService(IPairedBrowserStore store, TimeProvider? clock = null)
    {
        _store = store;
        _clock = clock ?? TimeProvider.System;
        NewCode();
    }

    public string Code { get; private set; } = string.Empty;

    public void NewCode() => Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public async Task<PairingResult> PairAsync(string code, string client)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (_pairingPausedUntil > now || (_lockedUntil.TryGetValue(client, out var until) && until > now))
            {
                return new PairingResult(PairingOutcome.LockedOut, null);
            }

            if (code.Trim() != Code)
            {
                var attempts = _wrongAttempts.TryGetValue(client, out var list) ? list : _wrongAttempts[client] = new List<DateTimeOffset>();
                attempts.RemoveAll(t => now - t > Window);
                attempts.Add(now);
                if (attempts.Count >= MaxWrongCodes)
                {
                    _lockedUntil[client] = now + Window;
                    attempts.Clear();
                }
                _allWrongAttempts.RemoveAll(t => now - t > Window);
                _allWrongAttempts.Add(now);
                if (_allWrongAttempts.Count >= MaxWrongCodesOverall)
                {
                    _pairingPausedUntil = now + Window;
                    _allWrongAttempts.Clear();
                    NewCode();
                }
                return new PairingResult(PairingOutcome.WrongCode, null);
            }

            _wrongAttempts.Remove(client);
            _lockedUntil.Remove(client);
            // A used code can't pair a second browser; the phone shows the new one.
            NewCode();
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await _store.AddAsync(Hash(token)).ConfigureAwait(false);
        return new PairingResult(PairingOutcome.Paired, token);
    }

    public async Task<bool> IsAuthorizedAsync(string? token) =>
        !string.IsNullOrEmpty(token) && await _store.ContainsAsync(Hash(token)).ConfigureAwait(false);

    public Task<int> PairedCountAsync() => _store.CountAsync();

    public Task UnpairAllAsync() => _store.ClearAsync();

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
