using System.Text;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class MmsPduParserTests
{
    // Tiny WSP byte writer so each test spells out the exact wire bytes it feeds the parser.
    private sealed class Pdu
    {
        private readonly List<byte> _bytes = new();
        public Pdu Octet(params int[] values) { foreach (var v in values) _bytes.Add((byte)v); return this; }
        public Pdu Text(string s) { _bytes.AddRange(Encoding.UTF8.GetBytes(s)); _bytes.Add(0); return this; }
        public Pdu Raw(byte[] data) { _bytes.AddRange(data); return this; }
        public Pdu Uintvar(int value)
        {
            var groups = new List<byte> { (byte)(value & 0x7F) };
            for (var rest = value >> 7; rest > 0; rest >>= 7) groups.Add((byte)((rest & 0x7F) | 0x80));
            groups.Reverse();
            _bytes.AddRange(groups);
            return this;
        }
        public Pdu EncodedUtf8(string s)
        {
            var text = Encoding.UTF8.GetBytes(s);
            return Octet(text.Length + 2, 0xEA).Raw(text).Octet(0);
        }
        public int Length => _bytes.Count;
        public byte[] ToArray() => _bytes.ToArray();
    }

    private static byte[] Part(byte[] contentTypeAndHeaders, byte[] data) =>
        new Pdu().Uintvar(contentTypeAndHeaders.Length).Uintvar(data.Length).Raw(contentTypeAndHeaders).Raw(data).ToArray();

    [Fact]
    public void ParseNotification_reads_the_download_location_and_sender()
    {
        var pdu = new Pdu()
            .Octet(0x8C, 0x82)                       // X-Mms-Message-Type: m-notification-ind
            .Octet(0x98).Text("T123")                // Transaction-Id
            .Octet(0x8D, 0x92)                       // MMS-Version 1.2
            .Octet(0x89, 24, 0x80).Text("+14707583374/TYPE=PLMN") // From: address-present + text-string
            .Octet(0x8A, 0x80)                       // Message-Class: personal
            .Octet(0x8E, 0x02, 0x10, 0x00)           // Message-Size: 4096
            .Octet(0x88, 0x05, 0x81, 0x03, 0x09, 0x3A, 0x80) // Expiry: relative 604800s
            .Octet(0x83).Text("http://mmsc.example.com/abc?id=1") // Content-Location
            .ToArray();

        var notification = MmsPduParser.ParseNotification(pdu);

        Assert.NotNull(notification);
        Assert.Equal("T123", notification.TransactionId);
        Assert.Equal("http://mmsc.example.com/abc?id=1", notification.ContentLocation);
        Assert.Equal("+14707583374", notification.From);
        Assert.Equal(4096, notification.MessageSize);
    }

    [Fact]
    public void ParseNotification_returns_null_for_a_different_message_type()
    {
        var pdu = new Pdu().Octet(0x8C, 0x86).Octet(0x98).Text("T1").ToArray(); // delivery-ind

        Assert.Null(MmsPduParser.ParseNotification(pdu));
    }

    [Fact]
    public void ParseRetrieved_reads_a_group_message_with_smil_text_and_image()
    {
        var smil = Encoding.UTF8.GetBytes("<smil><body><par><text src=\"text_0.txt\"/></par></body></smil>");
        var text = Encoding.UTF8.GetBytes("Hi group 👋");
        var jpeg = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray(); // >127 bytes: multi-octet uintvar

        var smilHeaders = new Pdu().Text("application/smil").Octet(0xC0).Text("<smil>").ToArray();
        var textHeaders = new Pdu().Octet(0x0F, 0x83, 0x81, 0xEA, 0x85).Text("text_0.txt").Octet(0x8E).Text("text_0.txt").ToArray();
        var imageHeaders = new Pdu().Octet(0x0F, 0x9E, 0x85).Text("IMG_0042.jpg").Octet(0x8E).Text("IMG_0042.jpg").ToArray();

        var body = new Pdu()
            .Uintvar(3)
            .Raw(Part(smilHeaders, smil))
            .Raw(Part(textHeaders, text))
            .Raw(Part(imageHeaders, jpeg))
            .ToArray();

        var contentTypeValue = new Pdu().Octet(0xB3, 0x8A).Text("<smil>").Octet(0x89).Text("application/smil").ToArray();

        var pdu = new Pdu()
            .Octet(0x8C, 0x84)                                   // m-retrieve-conf
            .Octet(0x98).Text("T123")
            .Octet(0x8D, 0x92)
            .Octet(0x8B).Text("msg-42@mmsc")                     // Message-ID
            .Octet(0x85, 0x04, 0x6A, 0xB1, 0x2C, 0x80)           // Date: 1789996160
            .Octet(0x89, 24, 0x80).Text("+14707583374/TYPE=PLMN") // From
            .Octet(0x97).EncodedUtf8("+17708654177/TYPE=PLMN")   // To (me)
            .Octet(0x97).Text("+16782628755/TYPE=PLMN")          // To (another person, plain text-string form)
            .Octet(0x82).EncodedUtf8("+14045550123/TYPE=PLMN")   // Cc
            .Octet(0x96).EncodedUtf8("Weekend plans")            // Subject
            .Octet(0x86, 0x81)                                   // Delivery-Report: no (skipped)
            .Octet(0x84).Octet(contentTypeValue.Length).Raw(contentTypeValue) // Content-Type: multipart.related
            .Raw(body)
            .ToArray();

        var message = MmsPduParser.ParseRetrieved(pdu);

        Assert.NotNull(message);
        Assert.Equal("+14707583374", message.From);
        Assert.Equal(new[] { "+17708654177", "+16782628755" }, message.To);
        Assert.Equal(new[] { "+14045550123" }, message.Cc);
        Assert.Equal("Weekend plans", message.Subject);
        Assert.Equal("msg-42@mmsc", message.MessageId);
        Assert.Equal("T123", message.TransactionId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(0x6AB12C80), message.Date);

        Assert.Equal(3, message.Parts.Count);
        Assert.Equal("application/smil", message.Parts[0].ContentType);
        Assert.Equal("<smil>", message.Parts[0].ContentId);
        Assert.Equal("text/plain", message.Parts[1].ContentType);
        Assert.Equal(106, message.Parts[1].Charset);
        Assert.Equal("Hi group 👋", message.Parts[1].Text);
        Assert.Equal("image/jpeg", message.Parts[2].ContentType);
        Assert.Equal("IMG_0042.jpg", message.Parts[2].Name);
        Assert.Equal(jpeg, message.Parts[2].Data);
        Assert.Null(message.Parts[2].Text);
    }

    [Fact]
    public void ParseRetrieved_falls_back_to_content_location_for_the_file_name_and_skips_application_headers()
    {
        var imageHeaders = new Pdu().Text("image/png").Octet(0x8E).Text("photo.png").ToArray();
        var body = new Pdu().Uintvar(1).Raw(Part(imageHeaders, new byte[] { 1, 2, 3 })).ToArray();

        var pdu = new Pdu()
            .Octet(0x8C, 0x84)
            .Octet(0x98).Text("T9")
            .Text("X-Carrier-Tag").Text("abc")                   // application header (text name + text value)
            .Octet(0x89, 0x01, 0x81)                             // From: insert-address-token
            .Octet(0x84, 0xA3)                                   // Content-Type: multipart.mixed (short form)
            .Raw(body)
            .ToArray();

        var message = MmsPduParser.ParseRetrieved(pdu);

        Assert.NotNull(message);
        Assert.Null(message.From);
        var part = Assert.Single(message.Parts);
        Assert.Equal("image/png", part.ContentType);
        Assert.Equal("photo.png", part.Name);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x8C })]
    [InlineData(new byte[] { 0x8C, 0x84, 0x98, 0x41, 0x42 })]            // unterminated text
    [InlineData(new byte[] { 0x8C, 0x84, 0x84, 0xA3, 0x05, 0x10, 0x7F })] // part lengths past the end
    public void ParseRetrieved_returns_null_for_truncated_input_instead_of_throwing(byte[] pdu)
    {
        Assert.Null(MmsPduParser.ParseRetrieved(pdu));
    }
}
