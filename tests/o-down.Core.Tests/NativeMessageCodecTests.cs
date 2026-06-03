using System.IO;
using System.Text;
using o_down.Core.Abstractions;
using o_down.Core.Protocol;
using Xunit;

namespace o_down.Core.Tests;

public class NativeMessageCodecTests
{
    [Fact]
    public void WrapWithLength_PrefixesFourByteLittleEndianLength()
    {
        var body = Encoding.UTF8.GetBytes("hello");
        var framed = NativeMessageCodec.WrapWithLength(body);
        Assert.Equal(body.Length + 4, framed.Length);
        Assert.Equal((byte)body.Length, framed[0]);
        Assert.Equal(0, framed[1]);
        Assert.Equal(0, framed[2]);
        Assert.Equal(0, framed[3]);
        Assert.Equal((byte)'h', framed[4]);
        Assert.Equal((byte)'o', framed[8]);
    }

    [Fact]
    public void WrapWithLength_RejectsOversize()
    {
        var huge = new byte[NativeMessageCodec.MaxMessageBytes + 1];
        Assert.Throws<InvalidOperationException>(() => NativeMessageCodec.WrapWithLength(huge));
    }

    [Fact]
    public async Task RoundTrip_RequestBody_MatchesCapturedLink()
    {
        using var ms = new MemoryStream();
        var link = new CapturedLink
        {
            Url = "https://example.com/file.zip",
            Referrer = "https://example.com/",
            Cookies = "sid=abc",
            FilenameHint = "file.zip",
            Source = "chrome-extension",
            CapturedAt = DateTimeOffset.UtcNow
        };
        var encoded = NativeMessageCodec.EncodeRequest(link);
        await ms.WriteAsync(encoded);
        ms.Position = 0;

        var decoded = await NativeMessageCodec.ReadRequestAsync(ms);
        Assert.NotNull(decoded);
        Assert.Equal(link.Url, decoded!.Url);
        Assert.Equal(link.Referrer, decoded.Referrer);
        Assert.Equal(link.Cookies, decoded.Cookies);
        Assert.Equal(link.FilenameHint, decoded.FilenameHint);
        Assert.Equal(link.Source, decoded.Source);
    }

    [Fact]
    public async Task RoundTrip_Response_PreservesAllFields()
    {
        using var ms = new MemoryStream();
        var resp = new NativeMessageCodec.NativeResponse
        {
            Ok = true,
            DownloadId = Guid.NewGuid(),
            Error = null,
            Version = "0.1.0"
        };
        var encoded = NativeMessageCodec.EncodeResponse(resp);
        await ms.WriteAsync(encoded);
        ms.Position = 0;

        var decoded = await NativeMessageCodec.ReadResponseAsync(ms);
        Assert.NotNull(decoded);
        Assert.True(decoded!.Ok);
        Assert.Equal(resp.DownloadId, decoded.DownloadId);
        Assert.Equal("0.1.0", decoded.Version);
    }

    [Fact]
    public async Task ReadRequestAsync_EmptyStreamReturnsNull()
    {
        using var ms = new MemoryStream();
        var result = await NativeMessageCodec.ReadRequestAsync(ms);
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadRequestAsync_TruncatedLengthIsNull()
    {
        using var ms = new MemoryStream(new byte[] { 0x05, 0x00 });
        var result = await NativeMessageCodec.ReadRequestAsync(ms);
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadRequestAsync_NegativeLengthIsNull()
    {
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        var result = await NativeMessageCodec.ReadRequestAsync(ms);
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadResponse_EmptyStreamReturnsNull()
    {
        using var ms = new MemoryStream();
        var result = await NativeMessageCodec.ReadResponseAsync(ms);
        Assert.Null(result);
    }

    [Fact]
    public void EncodeRequest_NullOptionalFieldsAreOmitted()
    {
        var link = new CapturedLink { Url = "https://x.example", Source = "s" };
        var bytes = NativeMessageCodec.EncodeRequest(link);
        var json = Encoding.UTF8.GetString(bytes, 4, bytes.Length - 4);
        Assert.Contains("\"url\"", json);
        Assert.Contains("\"source\"", json);
        Assert.DoesNotContain("\"referrer\"", json);
        Assert.DoesNotContain("\"cookies\"", json);
        Assert.DoesNotContain("\"filenameHint\"", json);
    }
}
