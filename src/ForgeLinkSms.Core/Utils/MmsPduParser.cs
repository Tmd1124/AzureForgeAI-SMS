using System.Text;

namespace ForgeLinkSms.Core.Utils;

public sealed record MmsNotification(string TransactionId, string ContentLocation, string? From, long? MessageSize);

public sealed record MmsPart(string ContentType, string? Name, string? ContentLocation, string? ContentId, int? Charset, byte[] Data)
{
    public string? Text => ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !ContentType.Equals("text/x-vcard", StringComparison.OrdinalIgnoreCase)
        ? MmsPduParser.Decode(Data, Charset)
        : null;
}

public sealed record MmsRetrieved(
    string? From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    DateTimeOffset? Date,
    string? Subject,
    string? MessageId,
    string? TransactionId,
    IReadOnlyList<MmsPart> Parts);

// Decodes the two incoming MMS PDUs the default SMS app must handle itself: M-Notification.ind
// (the WAP push saying "a message is waiting at this URL") and M-Retrieve.conf (the downloaded
// message). Android's own decoder (com.google.android.mms.pdu.PduParser) isn't in the public SDK.
// Byte layouts follow OMA-WAP-MMS-ENC and WAP-230-WSP, matching the constants MmsPduBuilder uses
// for the send side. Any malformed or truncated input returns null rather than throwing, because
// a bad PDU from a carrier must never crash the SMS receiver.
public static class MmsPduParser
{
    private const int MessageTypeNotificationInd = 0x82;
    private const int MessageTypeRetrieveConf = 0x84;

    private const int HeaderBcc = 0x81;
    private const int HeaderCc = 0x82;
    private const int HeaderContentLocation = 0x83;
    private const int HeaderContentType = 0x84;
    private const int HeaderDate = 0x85;
    private const int HeaderFrom = 0x89;
    private const int HeaderMessageId = 0x8B;
    private const int HeaderMessageType = 0x8C;
    private const int HeaderMessageSize = 0x8E;
    private const int HeaderSubject = 0x96;
    private const int HeaderTo = 0x97;
    private const int HeaderTransactionId = 0x98;

    private const int PartHeaderContentLocation = 0x8E;
    private const int PartHeaderContentId = 0xC0;

    private const int ParamCharset = 0x81;
    private const int ParamNameOld = 0x85;
    private const int ParamFilenameOld = 0x86;
    private const int ParamName = 0x97;
    private const int ParamFilename = 0x98;

    // WSP well-known content types, indexed by their short-integer code (PduContentTypes.java).
    private static readonly string[] WellKnownContentTypes =
    {
        "*/*", "text/*", "text/html", "text/plain", "text/x-hdml", "text/x-ttml", "text/x-vCalendar",
        "text/x-vCard", "text/vnd.wap.wml", "text/vnd.wap.wmlscript", "text/vnd.wap.wta-event", "multipart/*",
        "multipart/mixed", "multipart/form-data", "multipart/byterantes", "multipart/alternative",
        "application/*", "application/java-vm", "application/x-www-form-urlencoded", "application/x-hdmlc",
        "application/vnd.wap.wmlc", "application/vnd.wap.wmlscriptc", "application/vnd.wap.wta-eventc",
        "application/vnd.wap.uaprof", "application/vnd.wap.wtls-ca-certificate",
        "application/vnd.wap.wtls-user-certificate", "application/x-x509-ca-cert", "application/x-x509-user-cert",
        "image/*", "image/gif", "image/jpeg", "image/tiff", "image/png", "image/vnd.wap.wbmp",
        "application/vnd.wap.multipart.*", "application/vnd.wap.multipart.mixed",
        "application/vnd.wap.multipart.form-data", "application/vnd.wap.multipart.byteranges",
        "application/vnd.wap.multipart.alternative", "application/xml", "text/xml", "application/vnd.wap.wbxml",
        "application/x-x968-cross-cert", "application/x-x968-ca-cert", "application/x-x968-user-cert",
        "text/vnd.wap.si", "application/vnd.wap.sic", "text/vnd.wap.sl", "application/vnd.wap.slc",
        "text/vnd.wap.co", "application/vnd.wap.coc", "application/vnd.wap.multipart.related",
        "application/vnd.wap.sia", "text/vnd.wap.connectivity-xml", "application/vnd.wap.connectivity-wbxml",
        "application/pkcs7-mime", "application/vnd.wap.hashed-certificate", "application/vnd.wap.signed-certificate",
        "application/vnd.wap.cert-response", "application/xhtml+xml", "application/wml+xml", "text/css",
        "application/vnd.wap.mms-message"
    };

    private sealed class FormatException : Exception;

    public static MmsNotification? ParseNotification(byte[] pdu)
    {
        try
        {
            var reader = new Reader(pdu);
            var headers = ReadHeaders(reader, stopAtContentType: false);
            if (headers.MessageType != MessageTypeNotificationInd || headers.ContentLocation is null)
            {
                return null;
            }
            return new MmsNotification(headers.TransactionId ?? string.Empty, headers.ContentLocation, headers.From, headers.MessageSize);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static MmsRetrieved? ParseRetrieved(byte[] pdu)
    {
        try
        {
            var reader = new Reader(pdu);
            var headers = ReadHeaders(reader, stopAtContentType: true);
            if (headers.MessageType != MessageTypeRetrieveConf)
            {
                return null;
            }

            var parts = new List<MmsPart>();
            if (headers.ContentType is { } contentType)
            {
                if (contentType.StartsWith("application/vnd.wap.multipart", StringComparison.OrdinalIgnoreCase))
                {
                    parts.AddRange(ReadMultipart(reader));
                }
                else
                {
                    // A single-part body: the rest of the PDU is the content itself.
                    parts.Add(new MmsPart(contentType, null, null, null, headers.ContentCharset, reader.ReadRemaining()));
                }
            }

            return new MmsRetrieved(headers.From, headers.To, headers.Cc, headers.Date, headers.Subject,
                headers.MessageId, headers.TransactionId, parts);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string Decode(byte[] data, int? charset)
    {
        var encoding = charset switch
        {
            3 => Encoding.ASCII,
            4 => Encoding.Latin1,
            1000 => Encoding.BigEndianUnicode, // iso-10646-ucs-2
            1013 => Encoding.BigEndianUnicode, // utf-16be
            1015 => Encoding.Unicode,          // utf-16 (BOM decides)
            _ => Encoding.UTF8
        };
        return encoding.GetString(data).TrimEnd('\0');
    }

    private sealed class Headers
    {
        public int MessageType = -1;
        public string? TransactionId;
        public string? ContentLocation;
        public string? From;
        public readonly List<string> To = new();
        public readonly List<string> Cc = new();
        public DateTimeOffset? Date;
        public string? Subject;
        public string? MessageId;
        public long? MessageSize;
        public string? ContentType;
        public int? ContentCharset;
    }

    private static Headers ReadHeaders(Reader reader, bool stopAtContentType)
    {
        var headers = new Headers();
        while (!reader.AtEnd)
        {
            var field = reader.ReadByte();
            if (field < 0x80)
            {
                // Application-header: Token-text name (starting with this byte) + text value.
                reader.Back();
                reader.ReadText();
                reader.ReadText();
                continue;
            }

            switch (field)
            {
                case HeaderMessageType:
                    headers.MessageType = reader.ReadByte();
                    break;
                case HeaderTransactionId:
                    headers.TransactionId = reader.ReadText();
                    break;
                case HeaderContentLocation:
                    headers.ContentLocation = reader.ReadText();
                    break;
                case HeaderMessageId:
                    headers.MessageId = reader.ReadText();
                    break;
                case HeaderDate:
                    headers.Date = DateTimeOffset.FromUnixTimeSeconds(reader.ReadLongInteger());
                    break;
                case HeaderMessageSize:
                    headers.MessageSize = reader.ReadLongInteger();
                    break;
                case HeaderSubject:
                    headers.Subject = reader.ReadEncodedString();
                    break;
                case HeaderFrom:
                    headers.From = ReadFrom(reader);
                    break;
                case HeaderTo:
                    headers.To.Add(CleanAddress(reader.ReadEncodedString()));
                    break;
                case HeaderCc:
                    headers.Cc.Add(CleanAddress(reader.ReadEncodedString()));
                    break;
                case HeaderBcc:
                    reader.ReadEncodedString();
                    break;
                case HeaderContentType:
                    var (type, charset, _) = ReadContentType(reader);
                    headers.ContentType = type;
                    headers.ContentCharset = charset;
                    if (stopAtContentType)
                    {
                        return headers; // Content-Type is always the last header; the body follows.
                    }
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        return headers;
    }

    // From-value = Value-length (Address-present-token Encoded-string-value | Insert-address-token)
    private static string? ReadFrom(Reader reader)
    {
        var end = reader.ReadValueLength() + reader.Position;
        string? address = null;
        if (reader.ReadByte() == 0x80)
        {
            address = CleanAddress(reader.ReadEncodedString());
        }
        reader.Seek(end);
        return address;
    }

    // "+14707583374/TYPE=PLMN" → "+14707583374"
    private static string CleanAddress(string raw)
    {
        var slash = raw.IndexOf('/');
        return (slash >= 0 ? raw[..slash] : raw).Trim();
    }

    // Content-type-value = Constrained-media | Content-general-form
    // Content-general-form = Value-length Media-type *(Parameter)
    private static (string Type, int? Charset, string? Name) ReadContentType(Reader reader)
    {
        var first = reader.PeekByte();
        if (first >= 0x80)
        {
            reader.ReadByte();
            return (WellKnownType(first & 0x7F), null, null);
        }
        if (first >= 32)
        {
            return (reader.ReadText(), null, null);
        }

        var end = reader.ReadValueLength() + reader.Position;
        string type;
        var mediaFirst = reader.PeekByte();
        if (mediaFirst >= 0x80)
        {
            type = WellKnownType(reader.ReadByte() & 0x7F);
        }
        else if (mediaFirst >= 32)
        {
            type = reader.ReadText();
        }
        else
        {
            type = WellKnownType((int)reader.ReadLongInteger());
        }

        int? charset = null;
        string? name = null;
        while (reader.Position < end)
        {
            var token = reader.PeekByte();
            if (token < 0x80)
            {
                reader.ReadText();
                reader.SkipValue();
                continue;
            }
            reader.ReadByte();
            switch (token)
            {
                case ParamCharset:
                    charset = (int)reader.ReadInteger();
                    break;
                case ParamName or ParamNameOld or ParamFilename or ParamFilenameOld:
                    name ??= reader.ReadText();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.Seek(end);
        return (type, charset, name);
    }

    private static string WellKnownType(int code) =>
        code < WellKnownContentTypes.Length ? WellKnownContentTypes[code] : "application/octet-stream";

    // Multipart = nEntries *(HeadersLen DataLen ContentType Headers Data)
    private static List<MmsPart> ReadMultipart(Reader reader)
    {
        var count = reader.ReadUintvar();
        var parts = new List<MmsPart>();
        for (var i = 0; i < count; i++)
        {
            var headersLength = reader.ReadUintvar();
            var dataLength = reader.ReadUintvar();
            var headersEnd = reader.Position + headersLength;
            reader.Require(headersLength + dataLength);

            var (type, charset, name) = ReadContentType(reader);
            string? contentLocation = null;
            string? contentId = null;
            while (reader.Position < headersEnd)
            {
                var field = reader.ReadByte();
                if (field < 0x80)
                {
                    reader.Back();
                    reader.ReadText();
                    reader.ReadText();
                    continue;
                }
                switch (field)
                {
                    case PartHeaderContentLocation:
                        contentLocation = reader.ReadText();
                        break;
                    case PartHeaderContentId:
                        contentId = reader.ReadText();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.Seek(headersEnd);
            var data = reader.ReadBytes(dataLength);
            parts.Add(new MmsPart(type, name ?? contentLocation, contentLocation, contentId, charset, data));
        }
        return parts;
    }

    private sealed class Reader(byte[] data)
    {
        public int Position { get; private set; }
        public bool AtEnd => Position >= data.Length;

        public void Require(int count)
        {
            if (count < 0 || Position + count > data.Length)
            {
                throw new FormatException();
            }
        }

        public int PeekByte()
        {
            Require(1);
            return data[Position];
        }

        public int ReadByte()
        {
            Require(1);
            return data[Position++];
        }

        public void Back() => Position--;

        public void Seek(int position)
        {
            if (position < 0 || position > data.Length)
            {
                throw new FormatException();
            }
            Position = position;
        }

        public byte[] ReadBytes(int count)
        {
            Require(count);
            var bytes = data.AsSpan(Position, count).ToArray();
            Position += count;
            return bytes;
        }

        public byte[] ReadRemaining() => ReadBytes(data.Length - Position);

        // Text-string = [Quote] *TEXT End-of-string; Quoted-string starts with '"'.
        public string ReadText()
        {
            var start = Position;
            var end = Array.IndexOf(data, (byte)0, start);
            if (end < 0)
            {
                throw new FormatException();
            }
            Position = end + 1;
            if (start < end && (data[start] == 127 || data[start] == '"'))
            {
                start++;
            }
            return Encoding.UTF8.GetString(data, start, end - start);
        }

        // Encoded-string-value = Text-string | Value-length Char-set Text-string
        public string ReadEncodedString()
        {
            if (PeekByte() >= 32)
            {
                return ReadText();
            }
            var end = ReadValueLength() + Position;
            var charset = (int)ReadInteger();
            var textStart = Position;
            var textEnd = Array.IndexOf(data, (byte)0, textStart);
            if (textEnd < 0 || textEnd > end)
            {
                textEnd = end;
            }
            if (textStart < textEnd && data[textStart] == 127)
            {
                textStart++;
            }
            var text = Decode(data.AsSpan(textStart, Math.Max(0, textEnd - textStart)).ToArray(), charset);
            Seek(end);
            return text;
        }

        public int ReadValueLength()
        {
            var first = ReadByte();
            if (first < 31)
            {
                return first;
            }
            if (first == 31)
            {
                return ReadUintvar();
            }
            throw new FormatException();
        }

        public int ReadUintvar()
        {
            long value = 0;
            for (var i = 0; i < 5; i++)
            {
                var b = ReadByte();
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    return value > int.MaxValue ? throw new FormatException() : (int)value;
                }
            }
            throw new FormatException();
        }

        // Long-integer = Short-length(<=30) Multi-octet-integer (big-endian)
        public long ReadLongInteger()
        {
            var length = ReadByte();
            if (length > 8)
            {
                throw new FormatException();
            }
            long value = 0;
            for (var i = 0; i < length; i++)
            {
                value = (value << 8) | (uint)ReadByte();
            }
            return value;
        }

        // Integer-value = Short-integer | Long-integer
        public long ReadInteger() => PeekByte() >= 0x80 ? ReadByte() & 0x7F : ReadLongInteger();

        // Every MMS/WSP header value is one of: a Value-length-prefixed block, a Text-string, or a
        // single octet (Short-integer / token) — so any unrecognized value can be skipped by its
        // first byte alone.
        public void SkipValue()
        {
            var first = PeekByte();
            if (first <= 31)
            {
                var length = ReadValueLength();
                ReadBytes(length);
            }
            else if (first < 0x80)
            {
                ReadText();
            }
            else
            {
                ReadByte();
            }
        }
    }
}
