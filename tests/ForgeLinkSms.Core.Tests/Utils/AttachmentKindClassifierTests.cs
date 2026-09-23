using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class AttachmentKindClassifierTests
{
    [Theory]
    [InlineData("image/gif", AttachmentKind.Gif)]
    [InlineData("image/jpeg", AttachmentKind.Image)]
    [InlineData("image/png", AttachmentKind.Image)]
    [InlineData("video/mp4", AttachmentKind.Video)]
    [InlineData("text/x-vcard", AttachmentKind.Contact)]
    [InlineData("text/vcard", AttachmentKind.Contact)]
    [InlineData("application/pdf", AttachmentKind.File)]
    [InlineData(null, AttachmentKind.File)]
    public void FromContentType_classifies_the_content_type(string? contentType, AttachmentKind expected)
    {
        Assert.Equal(expected, AttachmentKindClassifier.FromContentType(contentType));
    }

    [Fact]
    public void FromContentType_is_case_insensitive()
    {
        Assert.Equal(AttachmentKind.Gif, AttachmentKindClassifier.FromContentType("IMAGE/GIF"));
        Assert.Equal(AttachmentKind.Video, AttachmentKindClassifier.FromContentType("VIDEO/MP4"));
    }
}
