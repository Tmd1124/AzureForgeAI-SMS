using Android.Content;
using SmsMessenger.Core.Data;
using SmsMessenger.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidBlockedNumberContract = global::Android.Provider.BlockedNumberContract;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace SmsMessenger.Platforms.Android;

public class ContactBlockService : IContactBlockService
{
    private readonly IBlockedNumberRepository _repository;

    public ContactBlockService(IBlockedNumberRepository repository)
    {
        _repository = repository;
    }

    public async Task BlockAsync(string phoneNumber)
    {
        await _repository.BlockAsync(phoneNumber);

        try
        {
            var context = AndroidApp.Context;
            var values = new AndroidContentValues();
            values.Put(AndroidBlockedNumberContract.BlockedNumbers.ColumnOriginalNumber, phoneNumber);
            context.ContentResolver!.Insert(AndroidBlockedNumberContract.BlockedNumbers.ContentUri!, values);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("SmsMessenger", $"BlockedNumberContract insert failed, falling back to local-only block: {ex.Message}");
        }
    }

    public async Task UnblockAsync(string phoneNumber)
    {
        await _repository.UnblockAsync(phoneNumber);

        try
        {
            AndroidBlockedNumberContract.Unblock(AndroidApp.Context, phoneNumber);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("SmsMessenger", $"BlockedNumberContract unblock failed: {ex.Message}");
        }
    }

    public Task<bool> IsBlockedAsync(string phoneNumber) => _repository.IsBlockedAsync(phoneNumber);

    public async Task<IReadOnlyList<string>> GetBlockedNumbersAsync()
    {
        var rows = await _repository.GetBlockedNumbersAsync();
        return rows.Select(r => r.PhoneNumber).ToList();
    }
}
