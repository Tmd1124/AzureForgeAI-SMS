using System.Net;
using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Utils;

public sealed record LinkPreview(string Url, string Title, string? Description, string? ImageUrl, string SiteName);

// Reads the Open Graph / Twitter card tags (and <title> as a fallback) that sites publish for
// exactly this kind of link preview.
public static partial class OpenGraphParser
{
    [GeneratedRegex(@"<meta\s[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex(@"([\w:.-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    public static LinkPreview? Parse(string url, string html)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match meta in MetaTagRegex().Matches(html))
        {
            string? key = null;
            string? content = null;
            foreach (Match attribute in AttributeRegex().Matches(meta.Value))
            {
                var name = attribute.Groups[1].Value;
                var value = attribute.Groups[2].Success ? attribute.Groups[2].Value : attribute.Groups[3].Value;
                if (name.Equals("property", StringComparison.OrdinalIgnoreCase) || name.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    key = value;
                }
                else if (name.Equals("content", StringComparison.OrdinalIgnoreCase))
                {
                    content = value;
                }
            }
            if (key is not null && !string.IsNullOrWhiteSpace(content))
            {
                tags.TryAdd(key, Clean(content));
            }
        }

        var title = First(tags, "og:title", "twitter:title")
            ?? (TitleRegex().Match(html) is { Success: true } t ? Clean(t.Groups[1].Value) : null);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var image = First(tags, "og:image", "og:image:url", "twitter:image");
        var imageUrl = image is not null && Uri.TryCreate(new Uri(url), image, out var resolved) ? resolved.ToString() : null;
        return new LinkPreview(url, title, First(tags, "og:description", "twitter:description", "description"), imageUrl,
            First(tags, "og:site_name") ?? Domain(url));
    }

    private static string? First(Dictionary<string, string> tags, params string[] keys) =>
        keys.Select(k => tags.TryGetValue(k, out var v) ? v : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Clean(string text) => WebUtility.HtmlDecode(text).Trim();

    private static string Domain(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
            : url;
}
