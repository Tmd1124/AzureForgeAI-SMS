using SmsMessenger.Core.Utils;

namespace SmsMessenger.Core.Tests.Utils;

public class ImageDataUriHelperTests
{
    [Fact]
    public async Task ToDataUriAsync_returns_null_when_path_is_null_or_empty()
    {
        Assert.Null(await ImageDataUriHelper.ToDataUriAsync(null));
        Assert.Null(await ImageDataUriHelper.ToDataUriAsync(string.Empty));
    }

    [Fact]
    public async Task ToDataUriAsync_returns_null_when_the_file_does_not_exist()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.jpg");

        Assert.Null(await ImageDataUriHelper.ToDataUriAsync(missingPath));
    }

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".bin", "image/jpeg")]
    public async Task ToDataUriAsync_encodes_the_file_with_the_expected_mime_type(string extension, string expectedMimeType)
    {
        var path = Path.Combine(Path.GetTempPath(), $"photo-{Guid.NewGuid()}{extension}");
        var bytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(path, bytes);

        try
        {
            var dataUri = await ImageDataUriHelper.ToDataUriAsync(path);

            Assert.Equal($"data:{expectedMimeType};base64,{Convert.ToBase64String(bytes)}", dataUri);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
