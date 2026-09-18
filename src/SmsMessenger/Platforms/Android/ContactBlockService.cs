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

        var context = AndroidApp.Context;
        var values = new AndroidContentValues();
        values.Put(AndroidBlockedNumberContract.BlockedNumbers.ColumnOriginalNumber, phoneNumber);
        context.ContentResolver!.Insert(AndroidBlockedNumberContract.BlockedNumbers.ContentUri!, values);
    }

    public async Task UnblockAsync(string phoneNumber)
    {
        await _repository.UnblockAsync(phoneNumber);

        AndroidBlockedNumberContract.Unblock(AndroidApp.Context, phoneNumber);
    }

    public Task<bool> IsBlockedAsync(string phoneNumber) => _repository.IsBlockedAsync(phoneNumber);

    public async Task<IReadOnlyList<string>> GetBlockedNumbersAsync()
    {
        var rows = await _repository.GetBlockedNumbersAsync();
        return rows.Select(r => r.PhoneNumber).ToList();
    }
}
