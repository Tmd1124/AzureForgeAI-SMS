using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidUri = global::Android.Net.Uri;
using AndroidContentValues = global::Android.Content.ContentValues;
using AndroidMediaStore = global::Android.Provider.MediaStore;
using AndroidEnvironment = global::Android.OS.Environment;
using AndroidMimeTypeMap = global::Android.Webkit.MimeTypeMap;

namespace ForgeLinkSms.Platforms.Android;

public class AttachmentSaveService : IAttachmentSaveService
{
    public Task<bool> SaveToDeviceAsync(MessageAttachment attachment) =>
        Task.Run(() => SaveCore(attachment));

    private static bool SaveCore(MessageAttachment attachment)
    {
        var context = AndroidApp.Context;
        var resolver = context.ContentResolver!;

        // Always re-read from the source part rather than reusing attachment.DataUri: DataUri is
        // only ever populated for images/GIFs under MmsReader's inline-size cap, so it's the only
        // path that also works for video/file attachments and for capped-out large images.
        byte[] data;
        try
        {
            using var input = resolver.OpenInputStream(AndroidUri.Parse($"content://mms/part/{attachment.PartId}")!);
            if (input is null)
            {
                return false;
            }
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            data = buffer.ToArray();
        }
        catch (Exception)
        {
            return false;
        }

        var contentType = GetContentType(attachment);
        var (collectionUri, relativeDir) = attachment.Kind switch
        {
            AttachmentKind.Image or AttachmentKind.Gif => (AndroidMediaStore.Images.Media.ExternalContentUri, AndroidEnvironment.DirectoryPictures),
            AttachmentKind.Video => (AndroidMediaStore.Video.Media.ExternalContentUri, AndroidEnvironment.DirectoryMovies),
            _ => (AndroidMediaStore.Downloads.ExternalContentUri, AndroidEnvironment.DirectoryDownloads)
        };

        var values = new AndroidContentValues();
        values.Put(AndroidMediaStore.IMediaColumns.DisplayName, attachment.FileName);
        values.Put(AndroidMediaStore.IMediaColumns.MimeType, contentType);
        // A dedicated subfolder keeps saved attachments easy to find instead of mixing them into
        // the top level of Pictures/Movies/Download alongside camera photos and other downloads.
        values.Put(AndroidMediaStore.IMediaColumns.RelativePath, $"{relativeDir}/ForgeLink SMS");

        try
        {
            var itemUri = resolver.Insert(collectionUri!, values);
            if (itemUri is null)
            {
                return false;
            }
            using var output = resolver.OpenOutputStream(itemUri);
            if (output is null)
            {
                return false;
            }
            output.Write(data, 0, data.Length);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string GetContentType(MessageAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName).TrimStart('.').ToLowerInvariant();
        return AndroidMimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? attachment.Kind switch
        {
            AttachmentKind.Image => "image/jpeg",
            AttachmentKind.Gif => "image/gif",
            AttachmentKind.Video => "video/mp4",
            _ => "application/octet-stream"
        };
    }
}
