using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidContactsContract = global::Android.Provider.ContactsContract;
using AndroidUri = global::Android.Net.Uri;

namespace SmsMessenger.Platforms.Android;

public class ContactService : IContactService
{
    public async Task<ContactInfo?> LookupAsync(string phoneNumber)
    {
        var context = AndroidApp.Context;
        var uri = AndroidUri.WithAppendedPath(AndroidContactsContract.PhoneLookup.ContentFilterUri, AndroidUri.Encode(phoneNumber));
        var projection = new[] { AndroidContactsContract.PhoneLookup.InterfaceConsts.DisplayName, AndroidContactsContract.PhoneLookup.InterfaceConsts.PhotoUri };

        using var cursor = context.ContentResolver!.Query(uri!, projection, null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        var name = cursor.GetString(cursor.GetColumnIndexOrThrow(AndroidContactsContract.PhoneLookup.InterfaceConsts.DisplayName));
        var photoIndex = cursor.GetColumnIndex(AndroidContactsContract.PhoneLookup.InterfaceConsts.PhotoUri);
        var photoUri = photoIndex >= 0 ? cursor.GetString(photoIndex) : null;

        return new ContactInfo
        {
            DisplayName = name ?? phoneNumber,
            PhoneNumber = phoneNumber,
            PhotoUri = await ToPhotoDataUriAsync(context, photoUri)
        };
    }

    // ContactsContract only ever gives back a content:// URI, which the BlazorWebView can't load
    // as an <img src> (same cross-origin restriction that affected the profile photo). Converting
    // to a data: URI here means every consumer of ContactInfo.PhotoUri gets something renderable.
    private static async Task<string?> ToPhotoDataUriAsync(global::Android.Content.Context context, string? photoUri)
    {
        if (string.IsNullOrEmpty(photoUri))
        {
            return null;
        }

        try
        {
            using var stream = context.ContentResolver!.OpenInputStream(AndroidUri.Parse(photoUri)!);
            if (stream is null)
            {
                return null;
            }

            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(memoryStream.ToArray())}";
        }
        catch (Exception)
        {
            return null;
        }
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
