using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using AndroidContentValues = global::Android.Content.ContentValues;
using AndroidPendingIntent = global::Android.App.PendingIntent;
using AndroidPendingIntentFlags = global::Android.App.PendingIntentFlags;
using AndroidSmsManager = global::Android.Telephony.SmsManager;
using AndroidTelephonyManager = global::Android.Telephony.TelephonyManager;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidUri = global::Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

// Incoming MMS arrive in two steps: a small WAP push saying "a message is waiting at this URL"
// (WapPushDeliverReceiver → Start), then the carrier download that SmsManager performs for us
// (MmsDownloadedReceiver → Complete). As the default SMS app, Android leaves it to this app to
// save the downloaded message into content://mms — nothing is stored if we don't.
internal static class MmsDownloader
{
    public const string FileExtra = "forgelink_mms_file";
    public const string FromExtra = "forgelink_mms_from";
    public const string LocationExtra = "forgelink_mms_location";
    public const string TransactionIdExtra = "forgelink_mms_tr_id";

    private const int MessageBoxInbox = 1;
    private const int MessageTypeRetrieveConf = 0x84;
    private const int AddressTypeFrom = 137;
    private const int AddressTypeTo = 151;
    private const int AddressTypeCc = 130;
    private const int CharsetUtf8 = 106;

    public static void Start(Context context, byte[] notificationPdu)
    {
        var notification = MmsPduParser.ParseNotification(notificationPdu);
        if (notification is null)
        {
            global::Android.Util.Log.Warn("ForgeLinkSms", "Ignoring WAP push that isn't a readable MMS notification");
            return;
        }

        if (!string.IsNullOrEmpty(notification.From) && IsBlocked(notification.From))
        {
            return;
        }

        // Carriers resend the notification if they don't hear back; never save the same message twice.
        if (!string.IsNullOrEmpty(notification.TransactionId) && IsAlreadyStored(context, notification.TransactionId))
        {
            return;
        }

        var file = new Java.IO.File(context.CacheDir, $"mms_download_{Guid.NewGuid():N}.dat");
        file.CreateNewFile();
        // Same FileProvider route as MmsSender: the system MMS service writes the download
        // through this content:// Uri (SmsManager grants it write access itself).
        var fileUri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.PackageName + ".fileprovider", file);

        var resultIntent = new Intent(context, typeof(MmsDownloadedReceiver)).SetPackage(context.PackageName);
        resultIntent.PutExtra(FileExtra, file.AbsolutePath);
        resultIntent.PutExtra(FromExtra, notification.From);
        resultIntent.PutExtra(LocationExtra, notification.ContentLocation);
        resultIntent.PutExtra(TransactionIdExtra, notification.TransactionId);
        var resultPending = AndroidPendingIntent.GetBroadcast(context, file.Name!.GetHashCode(), resultIntent,
            AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.OneShot)!;

        AndroidSmsManager.Default!.DownloadMultimediaMessage(context, notification.ContentLocation, fileUri, null, resultPending);
    }

    public static void Complete(Context context, bool succeeded, string? filePath, string? notifiedFrom, string? location, string? transactionId)
    {
        try
        {
            var message = succeeded && filePath is not null && File.Exists(filePath)
                ? MmsPduParser.ParseRetrieved(File.ReadAllBytes(filePath))
                : null;
            if (message is null)
            {
                NotifyDownloadFailed(context, notifiedFrom);
                return;
            }

            var from = message.From ?? notifiedFrom;
            if (!string.IsNullOrEmpty(from) && IsBlocked(from))
            {
                return;
            }

            var participants = MmsRecipients.OtherParticipants(from, message.To, message.Cc, SelfNumbers(context));
            if (participants.Count == 0)
            {
                return;
            }

            var threadId = AndroidTelephony.Threads.GetOrCreateThreadId(context, participants.ToList());
            Insert(context, message, threadId, location, transactionId);

            var groupLabel = participants.Count > 1 ? $"group of {participants.Count + 1}" : null;
            IncomingMessagePipeline.OnStored(context, threadId, from ?? participants[0], NotificationBody(message), groupLabel);
        }
        finally
        {
            if (filePath is not null)
            {
                File.Delete(filePath);
            }
        }
    }

    private static void Insert(Context context, MmsRetrieved message, long threadId, string? location, string? transactionId)
    {
        var resolver = context.ContentResolver!;
        var date = message.Date ?? DateTimeOffset.UtcNow;

        var values = new AndroidContentValues();
        values.Put("thread_id", threadId);
        values.Put("date", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        values.Put("date_sent", date.ToUnixTimeSeconds());
        values.Put("msg_box", MessageBoxInbox);
        values.Put("read", 0);
        values.Put("seen", 0);
        values.Put("m_type", MessageTypeRetrieveConf);
        values.Put("v", 0x12);
        values.Put("ct_t", "application/vnd.wap.multipart.related");
        values.Put("m_id", message.MessageId);
        values.Put("tr_id", message.TransactionId ?? transactionId);
        values.Put("ct_l", location);
        if (!string.IsNullOrEmpty(message.Subject))
        {
            values.Put("sub", message.Subject);
            values.Put("sub_cs", CharsetUtf8);
        }
        var messageUri = resolver.Insert(AndroidUri.Parse("content://mms")!, values);
        if (messageUri?.LastPathSegment is not { } idSegment || !long.TryParse(idSegment, out var messageId))
        {
            return;
        }

        var addrUri = AndroidUri.Parse($"content://mms/{messageId}/addr")!;
        void InsertAddress(string address, int type)
        {
            var addr = new AndroidContentValues();
            addr.Put("address", address);
            addr.Put("type", type);
            addr.Put("charset", CharsetUtf8);
            resolver.Insert(addrUri, addr);
        }
        if (!string.IsNullOrEmpty(message.From))
        {
            InsertAddress(message.From, AddressTypeFrom);
        }
        foreach (var to in message.To)
        {
            InsertAddress(to, AddressTypeTo);
        }
        foreach (var cc in message.Cc)
        {
            InsertAddress(cc, AddressTypeCc);
        }

        var partsUri = AndroidUri.Parse($"content://mms/{messageId}/part")!;
        foreach (var part in message.Parts)
        {
            var partValues = new AndroidContentValues();
            partValues.Put("ct", part.ContentType);
            partValues.Put("name", part.Name);
            partValues.Put("cl", part.ContentLocation ?? part.Name);
            partValues.Put("cid", part.ContentId);
            var isText = part.Text is not null || part.ContentType.Equals("application/smil", StringComparison.OrdinalIgnoreCase);
            if (isText)
            {
                // Text is stored decoded, so the stored charset is always UTF-8.
                partValues.Put("chset", CharsetUtf8);
                partValues.Put("text", part.Text ?? MmsPduParser.Decode(part.Data, part.Charset));
                resolver.Insert(partsUri, partValues);
                continue;
            }

            var partUri = resolver.Insert(partsUri, partValues);
            if (partUri is null)
            {
                continue;
            }
            using var output = resolver.OpenOutputStream(partUri);
            output?.Write(part.Data, 0, part.Data.Length);
        }
    }

    private static string NotificationBody(MmsRetrieved message)
    {
        var text = string.Join("\n", message.Parts.Select(p => p.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var attachment = message.Parts.FirstOrDefault(p => !p.ContentType.Equals("application/smil", StringComparison.OrdinalIgnoreCase));
        return attachment is null
            ? "New message"
            : AttachmentKindClassifier.FromContentType(attachment.ContentType) switch
            {
                AttachmentKind.Image => "📷 Photo",
                AttachmentKind.Gif => "GIF",
                AttachmentKind.Video => "🎬 Video",
                AttachmentKind.Contact => "👤 Contact",
                AttachmentKind.Audio => "🎤 Voice message",
                _ => "📎 Attachment"
            };
    }

    private static void NotifyDownloadFailed(Context context, string? from)
    {
        if (string.IsNullOrEmpty(from))
        {
            return;
        }
        var threadId = AndroidTelephony.Threads.GetOrCreateThreadId(context, from);
        var services = MauiApplication.Current.Services;
        var contact = services.GetRequiredService<IContactService>().LookupAsync(from).GetAwaiter().GetResult();
        services.GetRequiredService<INotificationService>().NotifyIncomingMessage(
            contact?.DisplayName ?? from,
            "Sent a picture or group message that couldn't be downloaded. Check mobile data, or ask them to send it again.",
            threadId,
            from);
    }

    private static bool IsBlocked(string address)
    {
        var blockService = MauiApplication.Current.Services.GetRequiredService<IContactBlockService>();
        return blockService.IsBlockedAsync(PhoneNumberFormatter.ToComparableDigits(address)).GetAwaiter().GetResult();
    }

    private static bool IsAlreadyStored(Context context, string transactionId)
    {
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse("content://mms")!, new[] { "_id" }, "tr_id = ?", new[] { transactionId }, null);
        return cursor is not null && cursor.Count > 0;
    }

    // The user's own number(s), so it can be left out of a group's participant list. Carriers
    // often don't expose it (Line1Number is empty), so past incoming MMS fill the gap: the user
    // is a recipient on every one of them.
    private static IReadOnlyList<string> SelfNumbers(Context context)
    {
        var numbers = new List<string>();
        try
        {
            var telephony = (AndroidTelephonyManager?)context.GetSystemService(Context.TelephonyService);
            if (!string.IsNullOrWhiteSpace(telephony?.Line1Number))
            {
                numbers.Add(telephony.Line1Number);
            }
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_PHONE_NUMBERS not granted — fall back to history below.
        }

        var recipientsPerMessage = new List<IReadOnlyList<string>>();
        using (var cursor = context.ContentResolver!.Query(AndroidUri.Parse("content://mms")!, new[] { "_id" }, "msg_box = 1", null, "date DESC LIMIT 30"))
        {
            while (cursor is not null && cursor.MoveToNext())
            {
                var id = cursor.GetLong(0);
                var recipients = new List<string>();
                using var addr = context.ContentResolver!.Query(AndroidUri.Parse($"content://mms/{id}/addr")!, new[] { "address" }, "type = ?", new[] { AddressTypeTo.ToString() }, null);
                while (addr is not null && addr.MoveToNext())
                {
                    if (addr.GetString(0) is { Length: > 0 } a && a != "insert-address-token")
                    {
                        recipients.Add(a);
                    }
                }
                recipientsPerMessage.Add(recipients);
            }
        }
        if (MmsRecipients.MostLikelySelfNumber(recipientsPerMessage) is { } guessed)
        {
            numbers.Add(guessed);
        }
        return numbers;
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public class MmsDownloadedReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        var succeeded = ResultCode == Result.Ok;
        var filePath = intent.GetStringExtra(MmsDownloader.FileExtra);
        var from = intent.GetStringExtra(MmsDownloader.FromExtra);
        var location = intent.GetStringExtra(MmsDownloader.LocationExtra);
        var transactionId = intent.GetStringExtra(MmsDownloader.TransactionIdExtra);

        var pendingResult = GoAsync();
        Task.Run(() =>
        {
            try
            {
                MmsDownloader.Complete(context, succeeded, filePath, from, location, transactionId);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("ForgeLinkSms", $"Saving downloaded MMS failed: {ex}");
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
