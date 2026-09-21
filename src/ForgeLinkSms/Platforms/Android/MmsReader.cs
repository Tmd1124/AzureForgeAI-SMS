using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;
using AndroidContext = global::Android.Content.Context;
using AndroidUri = global::Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

// MMS lives in a separate, much less documented set of content:// tables than SMS: the
// message row itself ("content://mms"), its sender/recipient rows ("content://mms/{id}/addr"),
// and its text/attachment parts ("content://mms/part", filtered by "mid = <id>"). None of this
// is exposed through the strongly-typed Android.Provider.Telephony.Mms bindings in a way that
// covers addr/part, so every column here is addressed by its raw, stable provider name — the
// same names every long-standing third-party SMS/MMS app on Android relies on.
internal static class MmsReader
{
    private const int MessageBoxInbox = 1;
    private const int MessageBoxSent = 2;
    private const int AddressTypeFrom = 137;
    private const int AddressTypeTo = 151;

    // Caps how large an inline image/GIF data: URI we'll build from an MMS part. Anything
    // bigger gets shown as a plain file chip instead of decoding multi-megabyte bytes into a
    // base64 string that both this process and the WebView would have to hold in memory.
    private const long MaxInlineAttachmentBytes = 5 * 1024 * 1024;

    public sealed class MmsSummary
    {
        public required long ThreadId { get; init; }
        public required long Id { get; init; }
        public required DateTimeOffset Date { get; init; }
        public required bool IsOutgoing { get; init; }
        public required bool IsRead { get; init; }
    }

    public static List<MmsSummary> QueryAll(AndroidContext context, long? threadId = null)
    {
        var results = new List<MmsSummary>();
        var mmsUri = AndroidUri.Parse("content://mms")!;
        var projection = new[] { "_id", "thread_id", "date", "read", "msg_box" };

        var selection = threadId is null
            ? "(msg_box = ? OR msg_box = ?)"
            : "(msg_box = ? OR msg_box = ?) AND thread_id = ?";
        var args = threadId is null
            ? new[] { MessageBoxInbox.ToString(), MessageBoxSent.ToString() }
            : new[] { MessageBoxInbox.ToString(), MessageBoxSent.ToString(), threadId.Value.ToString() };

        using var cursor = context.ContentResolver!.Query(mmsUri, projection, selection, args, "date DESC");
        if (cursor is null)
        {
            return results;
        }

        var idIdx = cursor.GetColumnIndexOrThrow("_id");
        var threadIdx = cursor.GetColumnIndexOrThrow("thread_id");
        var dateIdx = cursor.GetColumnIndexOrThrow("date");
        var readIdx = cursor.GetColumnIndexOrThrow("read");
        var boxIdx = cursor.GetColumnIndexOrThrow("msg_box");

        while (cursor.MoveToNext())
        {
            results.Add(new MmsSummary
            {
                Id = cursor.GetLong(idIdx),
                ThreadId = cursor.GetLong(threadIdx),
                // Unlike Sms.date (milliseconds), Mms.date is whole seconds since epoch.
                Date = DateTimeOffset.FromUnixTimeSeconds(cursor.GetLong(dateIdx)),
                IsRead = cursor.GetInt(readIdx) != 0,
                IsOutgoing = cursor.GetInt(boxIdx) == MessageBoxSent
            });
        }

        return results;
    }

    public static string GetAddress(AndroidContext context, long mmsId, bool isOutgoing)
    {
        var addrUri = AndroidUri.Parse($"content://mms/{mmsId}/addr")!;
        var projection = new[] { "address", "type" };
        using var cursor = context.ContentResolver!.Query(addrUri, projection, null, null, null);
        if (cursor is null)
        {
            return string.Empty;
        }

        var addressIdx = cursor.GetColumnIndexOrThrow("address");
        var typeIdx = cursor.GetColumnIndexOrThrow("type");
        var wantedType = isOutgoing ? AddressTypeTo : AddressTypeFrom;

        string? fallback = null;
        while (cursor.MoveToNext())
        {
            var address = cursor.GetString(addressIdx);
            if (string.IsNullOrEmpty(address) || address == "insert-address-token")
            {
                continue;
            }

            if (cursor.GetInt(typeIdx) == wantedType)
            {
                return address;
            }
            fallback ??= address;
        }

        return fallback ?? string.Empty;
    }

    public static (string Body, List<MessageAttachment> Attachments) GetContent(AndroidContext context, long mmsId)
    {
        var partUri = AndroidUri.Parse("content://mms/part")!;
        var projection = new[] { "_id", "ct", "text", "name", "cl" };
        using var cursor = context.ContentResolver!.Query(partUri, projection, "mid = ?", new[] { mmsId.ToString() }, null);

        var bodyParts = new List<string>();
        var attachments = new List<MessageAttachment>();
        if (cursor is null)
        {
            return (string.Empty, attachments);
        }

        var idIdx = cursor.GetColumnIndexOrThrow("_id");
        var ctIdx = cursor.GetColumnIndexOrThrow("ct");
        var textIdx = cursor.GetColumnIndexOrThrow("text");
        var nameIdx = cursor.GetColumnIndexOrThrow("name");
        var clIdx = cursor.GetColumnIndexOrThrow("cl");

        while (cursor.MoveToNext())
        {
            var contentType = cursor.GetString(ctIdx) ?? string.Empty;
            var partId = cursor.GetLong(idIdx);

            if (contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                var text = cursor.GetString(textIdx);
                if (string.IsNullOrEmpty(text))
                {
                    text = ReadPartAsText(context, partId);
                }
                if (!string.IsNullOrEmpty(text))
                {
                    bodyParts.Add(text);
                }
                continue;
            }

            if (contentType.Equals("application/smil", StringComparison.OrdinalIgnoreCase))
            {
                continue; // slideshow layout metadata, not user-facing content
            }

            var fileName = cursor.GetString(nameIdx) ?? cursor.GetString(clIdx) ?? $"attachment-{partId}";
            var kind = AttachmentKindClassifier.FromContentType(contentType);
            var dataUri = kind is AttachmentKind.Image or AttachmentKind.Gif
                ? ReadPartAsDataUri(context, partId, contentType)
                : null;

            attachments.Add(new MessageAttachment
            {
                FileName = fileName,
                Kind = kind,
                DataUri = dataUri
            });
        }

        return (string.Join("\n", bodyParts), attachments);
    }

    private static string? ReadPartAsText(AndroidContext context, long partId)
    {
        try
        {
            using var stream = context.ContentResolver!.OpenInputStream(AndroidUri.Parse($"content://mms/part/{partId}")!);
            if (stream is null)
            {
                return null;
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadPartAsDataUri(AndroidContext context, long partId, string contentType)
    {
        try
        {
            using var stream = context.ContentResolver!.OpenInputStream(AndroidUri.Parse($"content://mms/part/{partId}")!);
            if (stream is null)
            {
                return null;
            }
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            if (memoryStream.Length > MaxInlineAttachmentBytes)
            {
                return null;
            }
            return $"data:{contentType};base64,{Convert.ToBase64String(memoryStream.ToArray())}";
        }
        catch (Exception)
        {
            return null;
        }
    }
}
