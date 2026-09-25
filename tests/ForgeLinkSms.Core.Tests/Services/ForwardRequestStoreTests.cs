using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class ForwardRequestStoreTests
{
    [Fact]
    public void Take_hands_over_the_forwarded_message_once()
    {
        var store = new ForwardRequestStore();
        var photo = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };

        store.Set("look at this", photo);

        Assert.Equal(("look at this", photo), store.Take());
        Assert.Null(store.Take());
    }
}
