using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidContactsContract = global::Android.Provider.ContactsContract;
using AndroidUri = global::Android.Net.Uri;

namespace SmsMessenger.Platforms.Android;

public class ContactService : IContactService
{
    public Task<ContactInfo?> LookupAsync(string phoneNumber)
    {
        var context = AndroidApp.Context;
        var uri = AndroidUri.WithAppendedPath(AndroidContactsContract.PhoneLookup.ContentFilterUri, AndroidUri.Encode(phoneNumber));
        var projection = new[] { AndroidContactsContract.PhoneLookup.InterfaceConsts.DisplayName, AndroidContactsContract.PhoneLookup.InterfaceConsts.PhotoUri };

        using var cursor = context.ContentResolver!.Query(uri!, projection, null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return Task.FromResult<ContactInfo?>(null);
        }

        var name = cursor.GetString(cursor.GetColumnIndexOrThrow(AndroidContactsContract.PhoneLookup.InterfaceConsts.DisplayName));
        var photoIndex = cursor.GetColumnIndex(AndroidContactsContract.PhoneLookup.InterfaceConsts.PhotoUri);
        var photo = photoIndex >= 0 ? cursor.GetString(photoIndex) : null;

        return Task.FromResult<ContactInfo?>(new ContactInfo
        {
            DisplayName = name ?? phoneNumber,
            PhoneNumber = phoneNumber,
            PhotoUri = photo
        });
    }

    public Task<IReadOnlyList<ContactInfo>> GetAllContactsAsync()
    {
        var context = AndroidApp.Context;
        var results = new List<ContactInfo>();
        var projection = new[]
        {
            AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName,
            AndroidContactsContract.CommonDataKinds.Phone.Number,
            AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.PhotoUri
        };

        using var cursor = context.ContentResolver!.Query(AndroidContactsContract.CommonDataKinds.Phone.ContentUri!, projection, null, null, null);
        if (cursor is not null)
        {
            var nameIdx = cursor.GetColumnIndexOrThrow(AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName);
            var numberIdx = cursor.GetColumnIndexOrThrow(AndroidContactsContract.CommonDataKinds.Phone.Number);
            var photoIdx = cursor.GetColumnIndex(AndroidContactsContract.CommonDataKinds.Phone.InterfaceConsts.PhotoUri);

            while (cursor.MoveToNext())
            {
                var displayName = cursor.GetString(nameIdx);
                var number = cursor.GetString(numberIdx);
                if (string.IsNullOrEmpty(number))
                {
                    continue;
                }

                results.Add(new ContactInfo
                {
                    DisplayName = displayName ?? number,
                    PhoneNumber = number,
                    PhotoUri = photoIdx >= 0 ? cursor.GetString(photoIdx) : null
                });
            }
        }

        return Task.FromResult<IReadOnlyList<ContactInfo>>(results);
    }
}
