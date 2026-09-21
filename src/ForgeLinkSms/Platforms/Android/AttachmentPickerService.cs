using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Platforms.Android;

public class AttachmentPickerService : IAttachmentPickerService
{
    private static readonly FilePickerFileType ImageOrVideoType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.Android, new[] { "image/*", "video/*" } }
    });

    private static readonly FilePickerFileType GifType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.Android, new[] { "image/gif" } }
    });

    public Task<PickedAttachment?> PickImageOrVideoAsync() =>
        PickAsync(new PickOptions { PickerTitle = "Select a photo or video", FileTypes = ImageOrVideoType });

    public Task<PickedAttachment?> PickGifAsync() =>
        PickAsync(new PickOptions { PickerTitle = "Select a GIF", FileTypes = GifType });

    public Task<PickedAttachment?> PickFileAsync() =>
        PickAsync(new PickOptions { PickerTitle = "Select a file" });

    private static async Task<PickedAttachment?> PickAsync(PickOptions options)
    {
        var result = await FilePicker.Default.PickAsync(options);
        if (result is null)
        {
            return null;
        }

        var attachmentsDir = Path.Combine(FileSystem.AppDataDirectory, "attachments");
        Directory.CreateDirectory(attachmentsDir);
        var destinationPath = Path.Combine(attachmentsDir, result.FileName);

        using (var sourceStream = await result.OpenReadAsync())
        using (var destinationStream = File.Create(destinationPath))
        {
            await sourceStream.CopyToAsync(destinationStream);
        }

        return new PickedAttachment
        {
            FileName = result.FileName,
            LocalPath = destinationPath,
            Kind = AttachmentKindClassifier.FromContentType(result.ContentType)
        };
    }
}
