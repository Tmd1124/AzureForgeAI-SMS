using ForgeLinkSms.Core.Services;
using AndroidBitmapFactory = global::Android.Graphics.BitmapFactory;
using AndroidCompressFormat = global::Android.Graphics.Bitmap.CompressFormat;
using AndroidUri = global::Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

// Decodes photos at reduced size (BitmapFactory's inSampleSize) so a gallery of hundreds of
// photos never holds full-resolution images in memory or ships them to the WebView.
public class MediaThumbnailService : IMediaThumbnailService
{
    public Task<string?> GetImageDataUriAsync(long partId, int maxDimension) => Task.Run(() =>
    {
        try
        {
            var resolver = Platform.AppContext.ContentResolver!;
            var uri = AndroidUri.Parse($"content://mms/part/{partId}")!;

            var bounds = new AndroidBitmapFactory.Options { InJustDecodeBounds = true };
            using (var boundsStream = resolver.OpenInputStream(uri))
            {
                AndroidBitmapFactory.DecodeStream(boundsStream, null, bounds);
            }
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
            {
                return null;
            }

            var sampleSize = 1;
            while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sampleSize * 2) >= maxDimension)
            {
                sampleSize *= 2;
            }

            using var stream = resolver.OpenInputStream(uri);
            using var bitmap = AndroidBitmapFactory.DecodeStream(stream, null, new AndroidBitmapFactory.Options { InSampleSize = sampleSize });
            if (bitmap is null)
            {
                return null;
            }
            using var output = new MemoryStream();
            bitmap.Compress(AndroidCompressFormat.Jpeg!, 80, output);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(output.ToArray())}";
        }
        catch (Exception)
        {
            return (string?)null;
        }
    });
}
