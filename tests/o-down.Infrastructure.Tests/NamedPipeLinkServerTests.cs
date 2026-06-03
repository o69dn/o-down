using System.IO.Pipes;
using o_down.Core.Abstractions;
using o_down.Core.Protocol;
using o_down.Infrastructure;
using Xunit;

namespace o_down.Infrastructure.Tests;

public class NamedPipeLinkServerTests
{
    [Fact]
    public async Task PushLink_GetResponse_PreservesPayload()
    {
        var pipeName = "odown-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeLinkServer(pipeName: pipeName, maxConcurrentListeners: 1);
        var received = new TaskCompletionSource<CapturedLink>(TaskCreationOptions.RunContinuationsAsynchronously);
        var responderCalls = 0;
        server.SetResponder((link, _) =>
        {
            Interlocked.Increment(ref responderCalls);
            received.TrySetResult(link);
            return Task.FromResult(new NativeMessageCodec.NativeResponse { Ok = true, DownloadId = Guid.NewGuid() });
        });
        server.Start();
        await Task.Delay(50);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        var link = new CapturedLink
        {
            Url = "https://example.com/a.bin",
            Referrer = "https://example.com",
            Source = "chrome-extension",
            FilenameHint = "a.bin"
        };
        var req = NativeMessageCodec.EncodeRequest(link);
        await client.WriteAsync(req);
        await client.FlushAsync();
        var resp = await NativeMessageCodec.ReadResponseAsync(client);
        Assert.NotNull(resp);
        Assert.True(resp!.Ok);

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(link.Url, got.Url);
        Assert.Equal(link.FilenameHint, got.FilenameHint);
        Assert.Equal(1, Volatile.Read(ref responderCalls));
    }

    [Fact]
    public async Task PushLink_NoResponder_FiresLinkCapturedAndRespondsOk()
    {
        var pipeName = "odown-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeLinkServer(pipeName: pipeName, maxConcurrentListeners: 1);
        var fired = new TaskCompletionSource<CapturedLink>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.LinkCaptured += (_, link) => fired.TrySetResult(link);
        server.Start();
        await Task.Delay(50);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        var link = new CapturedLink { Url = "https://example.com/no-responder", Source = "firefox" };
        var req = NativeMessageCodec.EncodeRequest(link);
        await client.WriteAsync(req);
        await client.FlushAsync();
        var resp = await NativeMessageCodec.ReadResponseAsync(client);
        Assert.NotNull(resp);
        Assert.True(resp!.Ok);

        var got = await fired.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(link.Url, got.Url);
    }

    [Fact]
    public async Task Responder_Throws_ReturnsErrorResponse()
    {
        var pipeName = "odown-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeLinkServer(pipeName: pipeName, maxConcurrentListeners: 1);
        server.SetResponder((_, _) => throw new InvalidOperationException("boom"));
        server.Start();
        await Task.Delay(50);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        var req = NativeMessageCodec.EncodeRequest(new CapturedLink { Url = "https://x", Source = "test" });
        await client.WriteAsync(req);
        await client.FlushAsync();
        var resp = await NativeMessageCodec.ReadResponseAsync(client);
        Assert.NotNull(resp);
        Assert.False(resp!.Ok);
        Assert.Contains("boom", resp.Error ?? string.Empty);
    }
}
