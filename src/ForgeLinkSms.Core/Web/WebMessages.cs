using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ForgeLinkSms.Core.Web;

public sealed record WebRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query, string? Body, string? Token, string ClientAddress);

public sealed record WebResponse(int Status, string ContentType, byte[] Body)
{
    public static WebResponse Json(object value, int status = 200) =>
        new(status, "application/json", JsonSerializer.SerializeToUtf8Bytes(value, WebApi.JsonOptions));

    public static WebResponse Error(int status, string code) => Json(new { error = code }, status);
}

public interface IStaticFiles
{
    Task<(byte[] Content, string ContentType)?> GetAsync(string path);
}

public static class PrivateNetwork
{
    public static bool IsPrivate(string address)
    {
        if (!IPAddress.TryParse(address, out var ip))
        {
            return false;
        }
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }
        // Refused so another app on the phone can't reach the server without SMS permission.
        if (IPAddress.IsLoopback(ip))
        {
            return false;
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
        }
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }
}
