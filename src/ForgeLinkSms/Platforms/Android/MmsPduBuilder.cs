using System.Text;

namespace ForgeLinkSms.Platforms.Android;

// Hand-encodes a minimal MMS M-Send.req PDU (the OMA-WAP-MMS-ENC binary wire format) for a
// single recipient with optional text plus one image/video attachment. Android has no public API
// for building this PDU — SmsManager.SendMultimediaMessage only transports an already-encoded PDU
// file, it doesn't build one — and the real encoder classes (com.google.android.mms.pdu.*) are
// part of AOSP but excluded from the public SDK, so every app that sends MMS has to bring its own
// copy. Every header field code, content-type code, and binary primitive encoding below (Short-
// integer, Long-integer, Uintvar-integer, Value-length, Encoded-string-value, and the WSP
// multipart part-entry layout: [HeadersLen][DataLen][Headers][Data] per part) is verified byte-
// for-byte against the Apache-licensed klinker41/android-smsmms library's PduHeaders.java /
// PduComposer.java / PduContentTypes.java / PduPart.java — an AOSP-derived, widely-deployed
// reimplementation of Android's own internal MMS PDU classes — rather than derived from memory,
// since a wrong byte here fails silently at the carrier with no useful error.
internal static class MmsPduBuilder
{
    private const int MessageTypeSendReq = 0x80;
    private const int MmsVersion1_2 = (1 << 4) | 2; // 0x12
    private const int CharsetUtf8 = 106; // IANA MIBenum for UTF-8

    // Header field codes (PduHeaders.java).
    private const int HeaderMessageType = 0x8C;
    private const int HeaderTransactionId = 0x98;
    private const int HeaderMmsVersion = 0x8D;
    private const int HeaderDate = 0x85;
    private const int HeaderFrom = 0x89;
    private const int HeaderTo = 0x97;
    private const int HeaderContentType = 0x84;
    private const int FromInsertAddressToken = 0x81;

    // PduPart header field codes (PduPart.java).
    private const int PartName = 0x85;
    private const int PartCharset = 0x81;

    // Well-known short-integer content-type codes, i.e. their index in PduContentTypes'
    // registry table — only the ones this app can actually produce/attach.
    private const int ContentTypeTextPlain = 0x03;
    private const int ContentTypeMultipartMixed = 0x23;
    private const int ContentTypeImageGif = 0x1D;
    private const int ContentTypeImageJpeg = 0x1E;
    private const int ContentTypeImagePng = 0x20;

    public sealed class Attachment
    {
        public required string ContentType { get; init; }
        public required string FileName { get; init; }
        public required byte[] Data { get; init; }
    }

    public static byte[] BuildSendRequest(string toAddress, string? body, Attachment? attachment, string transactionId, DateTimeOffset date)
    {
        using var ms = new MemoryStream();

        AppendOctet(ms, HeaderMessageType);
        AppendOctet(ms, MessageTypeSendReq);

        AppendOctet(ms, HeaderTransactionId);
        AppendTextString(ms, transactionId);

        AppendOctet(ms, HeaderMmsVersion);
        AppendShortInteger(ms, MmsVersion1_2);

        AppendOctet(ms, HeaderDate);
        AppendLongInteger(ms, date.ToUnixTimeSeconds());

        // From: omit our own number and let the carrier fill it in via the well-defined
        // insert-address-token convention — there's no reliably queryable "my own MSISDN" API
        // across carriers/dual-SIM devices, and every MMS-capable carrier gateway accepts this.
        AppendOctet(ms, HeaderFrom);
        AppendValueLength(ms, 1);
        AppendOctet(ms, FromInsertAddressToken);

        AppendOctet(ms, HeaderTo);
        AppendEncodedString(ms, CharsetUtf8, toAddress + "/TYPE=PLMN");

        AppendOctet(ms, HeaderContentType);
        AppendBody(ms, body, attachment);

        return ms.ToArray();
    }

    private static void AppendBody(MemoryStream ms, string? body, Attachment? attachment)
    {
        // Content-Type header value: a Value-length wrapping just the multipart/mixed
        // short-integer. No "start"/"type" params — those exist only for multipart/related SMIL
        // slideshows, which this minimal single-attachment encoder doesn't build.
        AppendValueLength(ms, 1);
        AppendShortInteger(ms, ContentTypeMultipartMixed);

        var parts = new List<byte[]>();
        if (!string.IsNullOrEmpty(body))
        {
            parts.Add(BuildPart(wellKnown: true, ContentTypeTextPlain, "text/plain", "text_0.txt", Encoding.UTF8.GetBytes(body), CharsetUtf8));
        }
        if (attachment is not null)
        {
            var (wellKnown, code) = MapContentType(attachment.ContentType);
            parts.Add(BuildPart(wellKnown, code, attachment.ContentType, attachment.FileName, attachment.Data, charset: null));
        }

        AppendUintvarInteger(ms, parts.Count);
        foreach (var part in parts)
        {
            ms.Write(part, 0, part.Length);
        }
    }

    // One WSP multipart entry: [Uintvar HeadersLen][Uintvar DataLen][Headers][Data]. "Headers"
    // here is just the Content-Type value (itself Value-length-wrapped, with Name/Charset as
    // parameters inside that same wrapper) — Content-ID/Content-Location are omitted since Name
    // alone is enough for this app's own read side (MmsReader falls back name ?? cl).
    private static byte[] BuildPart(bool wellKnown, int contentTypeCode, string rawContentType, string name, byte[] data, int? charset)
    {
        using var contentTypeBlock = new MemoryStream();
        if (wellKnown)
        {
            AppendShortInteger(contentTypeBlock, contentTypeCode);
        }
        else
        {
            AppendTextString(contentTypeBlock, rawContentType);
        }
        AppendOctet(contentTypeBlock, PartName);
        AppendTextString(contentTypeBlock, name);
        if (charset is { } cs)
        {
            AppendOctet(contentTypeBlock, PartCharset);
            AppendShortInteger(contentTypeBlock, cs);
        }
        var contentTypeBytes = contentTypeBlock.ToArray();

        using var headers = new MemoryStream();
        AppendValueLength(headers, contentTypeBytes.Length);
        headers.Write(contentTypeBytes, 0, contentTypeBytes.Length);
        var headerBytes = headers.ToArray();

        using var entry = new MemoryStream();
        AppendUintvarInteger(entry, headerBytes.Length);
        AppendUintvarInteger(entry, data.Length);
        entry.Write(headerBytes, 0, headerBytes.Length);
        entry.Write(data, 0, data.Length);
        return entry.ToArray();
    }

    private static (bool WellKnown, int Code) MapContentType(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => (true, ContentTypeImageJpeg),
        "image/png" => (true, ContentTypeImagePng),
        "image/gif" => (true, ContentTypeImageGif),
        _ => (false, 0),
    };

    private static void AppendOctet(MemoryStream ms, int value) => ms.WriteByte((byte)value);

    // Short-integer = one octet, high bit set, value in the low 7 bits (WAP-230-WSP §8.4.2.1).
    private static void AppendShortInteger(MemoryStream ms, int value) => ms.WriteByte((byte)((value | 0x80) & 0xFF));

    // Long-integer = Short-length octet (byte count) + that many big-endian octets, minimum
    // octets needed to hold the value (WAP-230-WSP §8.4.2.2).
    private static void AppendLongInteger(MemoryStream ms, long value)
    {
        var bytes = new List<byte>();
        var remaining = value;
        while (remaining != 0 && bytes.Count < 8)
        {
            bytes.Insert(0, (byte)(remaining & 0xFF));
            remaining >>= 8;
        }
        if (bytes.Count == 0)
        {
            bytes.Add(0);
        }
        ms.WriteByte((byte)bytes.Count);
        foreach (var b in bytes)
        {
            ms.WriteByte(b);
        }
    }

    // Uintvar-integer: 7 bits per octet, most-significant group first, continuation bit (0x80)
    // set on every octet except the last (WAP-230-WSP §8.4.2.3).
    private static void AppendUintvarInteger(MemoryStream ms, long value)
    {
        var groups = new List<byte> { (byte)(value & 0x7F) };
        var remaining = value >> 7;
        while (remaining > 0)
        {
            groups.Add((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }
        groups.Reverse();
        foreach (var g in groups)
        {
            ms.WriteByte(g);
        }
    }

    // Value-length = Short-length (single octet, 0-30) | (Length-quote(31) + Uintvar-integer)
    // (WAP-230-WSP §8.4.2.4). Everything this encoder ever wraps is small, but the >=31 branch
    // is a plain spec requirement, not a defensive nicety.
    private static void AppendValueLength(MemoryStream ms, int length)
    {
        if (length < 31)
        {
            ms.WriteByte((byte)length);
            return;
        }
        ms.WriteByte(31);
        AppendUintvarInteger(ms, length);
    }

    // Text-string = [Quote] *TEXT End-of-string. A leading byte >=128 would be ambiguous with a
    // following primitive, so it gets escaped with the Quote octet (127) first; the string is
    // always NUL-terminated (WAP-230-WSP §8.4.2.1).
    private static void AppendTextString(MemoryStream ms, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 0 && bytes[0] > 127)
        {
            ms.WriteByte(127);
        }
        ms.Write(bytes, 0, bytes.Length);
        ms.WriteByte(0);
    }

    // Encoded-string-value = Value-length Char-set Text-string (OMA-WAP-MMS-ENC §7.2.32).
    private static void AppendEncodedString(MemoryStream ms, int charset, string text)
    {
        using var sub = new MemoryStream();
        AppendShortInteger(sub, charset);
        AppendTextString(sub, text);
        var subBytes = sub.ToArray();
        AppendValueLength(ms, subBytes.Length);
        ms.Write(subBytes, 0, subBytes.Length);
    }
}
