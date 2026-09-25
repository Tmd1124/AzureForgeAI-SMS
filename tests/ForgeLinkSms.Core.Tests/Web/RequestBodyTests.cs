using System.Text;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.Tests.Web;

public class RequestBodyTests
{
    [Fact]
    public async Task A_small_body_is_read()
    {
        var body = await RequestBody.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("{\"code\":\"123456\"}")), 64, CancellationToken.None);

        Assert.Equal("{\"code\":\"123456\"}", body);
    }

    [Fact]
    public async Task A_body_over_the_limit_is_refused_without_reading_it_all()
    {
        var stream = new EndlessStream();

        var body = await RequestBody.ReadAsync(stream, 1024, CancellationToken.None);

        Assert.Null(body);
        Assert.True(stream.BytesRead <= 1024 + 8192);
    }

    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'a', offset, count);
            BytesRead += count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
