using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class AttachmentTextFormatterTests
{
    [Fact]
    public void FormatLocationLink_builds_a_google_maps_query_link()
    {
        var link = AttachmentTextFormatter.FormatLocationLink(33.7490, -84.3880);

        Assert.Equal("https://maps.google.com/?q=33.749,-84.388", link);
    }

    [Fact]
    public void FormatLocationLink_uses_invariant_culture_regardless_of_current_culture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            var link = AttachmentTextFormatter.FormatLocationLink(33.7490, -84.3880);
            Assert.DoesNotContain(",-84", link.Replace(",-84.388", "")); // sanity: no locale comma-decimal corruption
            Assert.Equal("https://maps.google.com/?q=33.749,-84.388", link);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void FormatContactShare_builds_a_vcard_text_block_with_e164_number()
    {
        var contact = new ContactInfo { DisplayName = "Alice Smith", PhoneNumber = "5550148890" };

        var text = AttachmentTextFormatter.FormatContactShare(contact);

        Assert.Equal("BEGIN:VCARD\nVERSION:3.0\nFN:Alice Smith\nTEL:+15550148890\nEND:VCARD", text);
    }

    [Fact]
    public void FormatContactShare_normalizes_numbers_already_carrying_a_country_code()
    {
        var contact = new ContactInfo { DisplayName = "Bob Jones", PhoneNumber = "+1 (555) 014-2231" };

        var text = AttachmentTextFormatter.FormatContactShare(contact);

        Assert.Contains("TEL:+15550142231", text);
    }
}
