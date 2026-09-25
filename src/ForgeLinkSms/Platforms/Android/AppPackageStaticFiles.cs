using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Platforms.Android;

// Serves the browser app bundled at Resources/Raw/web/.
public class AppPackageStaticFiles : IStaticFiles
{
    public async Task<(byte[] Content, string ContentType)?> GetAsync(string path)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync($"web/{path}");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var contentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
            return (buffer.ToArray(), contentType);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or Java.IO.FileNotFoundException)
        {
            return null;
        }
    }
}
