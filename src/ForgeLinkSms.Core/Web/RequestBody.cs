using System.Text;

namespace ForgeLinkSms.Core.Web;

public static class RequestBody
{
    // Chunked uploads have no Content-Length, so the limit has to be enforced while reading.
    public static async Task<string?> ReadAsync(Stream input, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
