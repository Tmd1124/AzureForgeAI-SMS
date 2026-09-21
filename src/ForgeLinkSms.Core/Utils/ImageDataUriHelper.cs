namespace ForgeLinkSms.Core.Utils;

public static class ImageDataUriHelper
{
    public static async Task<string?> ToDataUriAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(filePath);
        var mimeType = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/jpeg"
        };

        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }
}
