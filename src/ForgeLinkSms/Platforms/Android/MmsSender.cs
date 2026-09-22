using AndroidApp = global::Android.App.Application;
using AndroidContentValues = global::Android.Content.ContentValues;
using AndroidUri = global::Android.Net.Uri;
using AndroidSmsManager = global::Android.Telephony.SmsManager;
using AndroidContext = global::Android.Content.Context;
using AndroidMimeTypeMap = global::Android.Webkit.MimeTypeMap;

namespace ForgeLinkSms.Platforms.Android;

// Orchestrates an outgoing MMS: encode the PDU (MmsPduBuilder), hand it to the OS for carrier
// transport, and separately write our own copy into content://mms so it shows up in the thread
// immediately — Android does not do this automatically even for the default SMS app, exactly
// like plain SMS sending in SmsService.SendAsync above.
internal static class MmsSender
{
    public static Task SendAsync(long threadId, string address, string? body, string attachmentLocalPath, string attachmentFileName) =>
        Task.Run(() => SendCore(threadId, address, body, attachmentLocalPath, attachmentFileName));

    private static void SendCore(long threadId, string address, string? body, string attachmentLocalPath, string attachmentFileName)
    {
        var context = AndroidApp.Context;
        var contentType = GetContentType(attachmentLocalPath);
        var data = File.ReadAllBytes(attachmentLocalPath);
        var transactionId = Guid.NewGuid().ToString("N");
        var date = DateTimeOffset.UtcNow;

        var pdu = MmsPduBuilder.BuildSendRequest(
            address,
            body,
            new MmsPduBuilder.Attachment { ContentType = contentType, FileName = attachmentFileName, Data = data },
            transactionId,
            date);

        var pduFile = new Java.IO.File(context.CacheDir, $"mms_send_{transactionId}.dat");
        using (var stream = new FileStream(pduFile.AbsolutePath!, FileMode.Create))
        {
            stream.Write(pdu, 0, pdu.Length);
        }

        // SmsManager.SendMultimediaMessage reads the PDU via ContentResolver as the calling
        // (system MMS service) identity, not this app's own — a raw file:// Uri would throw
        // FileUriExposedException on API 24+, so it has to go through a FileProvider content://
        // Uri with read permission granted (grantUriPermissions="true" on the provider, declared
        // in AndroidManifest.xml).
        var authority = context.PackageName + ".fileprovider";
        var pduUri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, pduFile);

        AndroidSmsManager.Default!.SendMultimediaMessage(context, pduUri, null, null, null);

        InsertSentMessage(context, threadId, address, body, date, contentType, attachmentFileName, data);
    }

    // Mirrors MmsReader's read-side schema exactly (content://mms, .../addr, .../part with the
    // same raw column names) so a message this app just sent renders identically to one it read
    // back from a real received/sent MMS.
    private static void InsertSentMessage(AndroidContext context, long threadId, string address, string? body,
        DateTimeOffset date, string attachmentContentType, string attachmentFileName, byte[] attachmentData)
    {
        var resolver = context.ContentResolver!;

        var messageValues = new AndroidContentValues();
        messageValues.Put("thread_id", threadId);
        messageValues.Put("date", date.ToUnixTimeSeconds());
        messageValues.Put("msg_box", 2); // MmsReader.MessageBoxSent
        messageValues.Put("read", 1);
        messageValues.Put("m_type", 0x80); // MESSAGE_TYPE_SEND_REQ — matches the wire PDU's own type.
        var messageUri = resolver.Insert(AndroidUri.Parse("content://mms")!, messageValues);
        if (messageUri?.LastPathSegment is not { } idSegment || !long.TryParse(idSegment, out var msgId))
        {
            return;
        }

        var addrValues = new AndroidContentValues();
        addrValues.Put("address", address);
        addrValues.Put("type", 151); // MmsReader.AddressTypeTo
        addrValues.Put("charset", 106); // UTF-8
        resolver.Insert(AndroidUri.Parse($"content://mms/{msgId}/addr")!, addrValues);

        if (!string.IsNullOrEmpty(body))
        {
            var textValues = new AndroidContentValues();
            textValues.Put("ct", "text/plain");
            textValues.Put("text", body);
            resolver.Insert(AndroidUri.Parse($"content://mms/{msgId}/part")!, textValues);
        }

        var partValues = new AndroidContentValues();
        partValues.Put("ct", attachmentContentType);
        partValues.Put("name", attachmentFileName);
        var partUri = resolver.Insert(AndroidUri.Parse($"content://mms/{msgId}/part")!, partValues);
        if (partUri is not null)
        {
            using var output = resolver.OpenOutputStream(partUri);
            output?.Write(attachmentData, 0, attachmentData.Length);
        }
    }

    private static string GetContentType(string localPath)
    {
        var extension = Path.GetExtension(localPath).TrimStart('.').ToLowerInvariant();
        return AndroidMimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? "application/octet-stream";
    }
}
