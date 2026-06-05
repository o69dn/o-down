using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using RichardSzalay.MockHttp;
using o_down.Core.Models;
using o_down.Engines.Aria2;
using Xunit;

namespace o_down.Engines.Aria2.Tests;

public class Aria2JsonRpcClientTests
{
    [Fact]
    public async Task CallAsync_PostsValidRpcEnvelope_AndReturnsResult()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("http://127.0.0.1:6800/jsonrpc")
            .Respond("application/json",
                """{"jsonrpc":"2.0","id":1,"result":"abc123"}""");
        await using var client = new Aria2JsonRpcClient(new Uri("http://127.0.0.1:6800/jsonrpc"), "secret");
        SwapHttp(client, mock);

        var result = await client.CallAsync("aria2.addUri", new object?[]
        {
            new object[] { "https://example.com/file.zip" },
            new Dictionary<string, object?> { ["dir"] = @"C:\Downloads" }
        });

        Assert.Equal("abc123", result?.GetValue<string>());
    }

    [Fact]
    public async Task CallAsync_Throws_OnErrorResponse()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("http://127.0.0.1:6800/jsonrpc")
            .Respond(HttpStatusCode.InternalServerError, "application/json", "boom");
        await using var client = new Aria2JsonRpcClient(new Uri("http://127.0.0.1:6800/jsonrpc"));
        SwapHttp(client, mock);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CallAsync("aria2.tellActive", Array.Empty<object?>()));
    }

    [Fact]
    public async Task CallAsync_PrependsToken_WhenSecretConfigured()
    {
        string? capturedBody = null;
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":"ok"}""")
            };
        });
        await using var client = new Aria2JsonRpcClient(new Uri("http://127.0.0.1:6800/jsonrpc"), "s3cr3t");
        SwapHttp(client, mock);

        await client.CallAsync("aria2.getVersion", Array.Empty<object?>());

        Assert.NotNull(capturedBody);
        var body = JsonNode.Parse(capturedBody!)!;
        var firstParam = body["params"]![0]!.GetValue<string>();
        Assert.Equal("token:s3cr3t", firstParam);
    }

    [Fact]
    public async Task MultiCallAsync_BatchesAndUnwraps()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("http://127.0.0.1:6800/jsonrpc")
            .Respond("application/json", """
                {
                  "jsonrpc":"2.0","id":1,
                  "result":[
                    [{"gid":"a","status":"active"}],
                    [{"gid":"b","status":"complete"}]
                  ]
                }
                """);
        await using var client = new Aria2JsonRpcClient(new Uri("http://127.0.0.1:6800/jsonrpc"));
        SwapHttp(client, mock);

        var results = await client.MultiCallAsync(new (string, object?[])[]
        {
            ("aria2.tellStatus", new object?[] { "a", Array.Empty<string>() }),
            ("aria2.tellStatus", new object?[] { "b", Array.Empty<string>() })
        });

        Assert.Equal(2, results.Count);
        Assert.Equal("a", results[0]?["gid"]?.GetValue<string>());
        Assert.Equal("complete", results[1]?["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task TellStatusAsync_SendsKeys()
    {
        string? capturedBody = null;
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":{"gid":"g1","status":"active"}}""")
            };
        });
        await using var client = new Aria2JsonRpcClient(new Uri("http://127.0.0.1:6800/jsonrpc"));
        SwapHttp(client, mock);

        var res = await client.TellStatusAsync("g1", new[] { "gid", "status" });

        Assert.Equal("active", res?["status"]?.GetValue<string>());
        var body = JsonNode.Parse(capturedBody!)!;
        var prm = body["params"]!.AsArray();
        Assert.Equal("g1", prm[1]!.GetValue<string>());
        Assert.Equal("gid", prm[2]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task ChangeOptionAsync_WrapsOptionsObject()
    {
        string? capturedBody = null;
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":"OK"}""")
            };
        });
        await using var client = new Aria2JsonRpcClient(new Uri("http://127.0.0.1:6800/jsonrpc"));
        SwapHttp(client, mock);

        await client.ChangeOptionAsync("g1", new Dictionary<string, object?>
        {
            ["max-download-limit"] = "1M",
            ["split"] = 8
        });

        var body = JsonNode.Parse(capturedBody!)!;
        var prm = body["params"]!.AsArray();
        Assert.Equal("g1", prm[1]!.GetValue<string>());
        var opts = prm[2]!.AsObject();
        Assert.Equal("1M", opts["max-download-limit"]!.GetValue<string>());
        Assert.Equal(8, opts["split"]!.GetValue<int>());
    }

    [Fact]
    public async Task AddUri_OneDimensional_UrisArray_AndPerTaskOptions()
    {
        string? capturedBody = null;
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":"abc123"}""")
            };
        });
        await using var client = new Aria2JsonRpcClient(new Uri("http://127.0.0.1:6800/jsonrpc"), "tok");
        SwapHttp(client, mock);

        var uris = new[] { "https://example.com/a.zip", "https://mirror.example.com/a.zip" };
        var options = new Dictionary<string, object?>
        {
            ["dir"] = @"C:\Downloads",
            ["out"] = "a.zip",
            ["split"] = 8,
            ["max-connection-per-server"] = 4,
            ["max-download-limit"] = 2L * 1024 * 1024
        };
        var gid = await client.CallAsync("aria2.addUri", uris, options);

        Assert.Equal("abc123", gid?.GetValue<string>());
        Assert.NotNull(capturedBody);
        var body = JsonNode.Parse(capturedBody!)!;
        var prm = body["params"]!.AsArray();

        Assert.Equal("token:tok", prm[0]!.GetValue<string>());

        var urisNode = prm[1]!.AsArray();
        Assert.Equal(2, urisNode.Count);
        Assert.Equal("https://example.com/a.zip", urisNode[0]!.GetValue<string>());
        Assert.Equal("https://mirror.example.com/a.zip", urisNode[1]!.GetValue<string>());

        var optsNode = prm[2]!.AsObject();
        Assert.Equal(@"C:\Downloads", optsNode["dir"]!.GetValue<string>());
        Assert.Equal("a.zip", optsNode["out"]!.GetValue<string>());
        Assert.Equal(8, optsNode["split"]!.GetValue<int>());
        Assert.Equal(4, optsNode["max-connection-per-server"]!.GetValue<int>());
        Assert.Equal(2L * 1024 * 1024, optsNode["max-download-limit"]!.GetValue<long>());
    }

    private static void SwapHttp(Aria2JsonRpcClient client, MockHttpMessageHandler mock)
    {
        typeof(Aria2JsonRpcClient)
            .GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(client, new HttpClient(mock) { BaseAddress = new Uri("http://127.0.0.1:6800/jsonrpc") });
    }
}

public class Aria2OptionsTests
{
    [Fact]
    public void FromDownloadItem_CopiesScalarAndStringFields()
    {
        var item = new DownloadItem
        {
            SourceUrl = "https://example.com/x.zip",
            DestinationDirectory = @"C:\D",
            FilenameHint = "x.zip",
            MaxConnections = 8,
            MaxConnectionsPerServer = 4,
            MinSplitSize = 2L * 1024 * 1024,
            MaxTries = 3,
            RetryWait = TimeSpan.FromSeconds(10),
            MaxDownloadLimit = 1024 * 1024,
            LowestSpeedLimit = 1024,
            Proxy = "http://127.0.0.1:8888",
            UserAgent = "ua/1",
            Checksum = "deadbeef",
            ChecksumAlgorithm = "sha-256",
            ReferrerUrl = "https://ref.example"
        };

        var o = Aria2Options.FromDownloadItem(item);
        var rpc = o.ToRpcOptions();

        Assert.Equal(8, (int)(rpc["split"]!));
        Assert.Equal(4, (int)(rpc["max-connection-per-server"]!));
        Assert.Equal(2L * 1024 * 1024, (long)(rpc["min-split-size"]!));
        Assert.Equal(3, (int)(rpc["max-tries"]!));
        Assert.Equal("1M", rpc["max-download-limit"]);
        Assert.Equal("1K", rpc["lowest-speed-limit"]);
        Assert.Equal("http://127.0.0.1:8888", rpc["all-proxy"]);
        Assert.Equal("ua/1", rpc["user-agent"]);
        Assert.Equal("sha-256=deadbeef", rpc["checksum"]);
        var headerArr = (string[])rpc["header"]!;
        Assert.Contains("Referer: https://ref.example", headerArr);
    }

    [Fact]
    public void FromDownloadItem_OmitsNullFields()
    {
        var item = new DownloadItem { SourceUrl = "https://e/x", DestinationDirectory = @"C:\D" };
        var rpc = Aria2Options.FromDownloadItem(item).ToRpcOptions();

        Assert.DoesNotContain("split", rpc.Keys);
        Assert.DoesNotContain("max-connection-per-server", rpc.Keys);
        Assert.DoesNotContain("checksum", rpc.Keys);
        Assert.Contains("dir", rpc.Keys);
        Assert.Contains("user-agent", rpc.Keys);
    }

    [Fact]
    public void ToRpcOptionsDelta_ReturnsOnlyChangedKeys()
    {
        var a = Aria2Options.FromDownloadItem(new DownloadItem
        {
            SourceUrl = "https://e/x",
            DestinationDirectory = "d",
            MaxConnections = 8,
            MaxConnectionsPerServer = 4
        });
        var b = Aria2Options.FromDownloadItem(new DownloadItem
        {
            SourceUrl = "https://e/x",
            DestinationDirectory = "d",
            MaxConnections = 16,
            MaxConnectionsPerServer = 4
        });

        var delta = b.ToRpcOptionsDelta(a);
        Assert.Single(delta);
        Assert.Equal(16, delta["split"]);
    }

    [Fact]
    public void ToRpcOptions_FormatsBytes_AsAria2SpeedString()
    {
        var o = new Aria2Options
        {
            MaxDownloadLimit = 5L * 1024 * 1024,
            MaxUploadLimit = 512L * 1024,
            LowestSpeedLimit = 1024
        };
        var rpc = o.ToRpcOptions();
        Assert.Equal("5M", rpc["max-download-limit"]);
        Assert.Equal("512K", rpc["max-upload-limit"]);
        Assert.Equal("1K", rpc["lowest-speed-limit"]);
    }

    [Fact]
    public void ToRpcOptions_OmitsNullLimits()
    {
        var rpc = new Aria2Options().ToRpcOptions();
        Assert.False(rpc.ContainsKey("max-download-limit"));
        Assert.False(rpc.ContainsKey("max-upload-limit"));
        Assert.False(rpc.ContainsKey("lowest-speed-limit"));
    }

    [Fact]
    public void FromDownloadItem_CopiesMaxUploadLimit()
    {
        var item = new DownloadItem
        {
            SourceUrl = "u",
            DestinationDirectory = "d",
            MaxDownloadLimit = 1024 * 1024,
            MaxUploadLimit = 256 * 1024
        };
        var o = Aria2Options.FromDownloadItem(item);
        Assert.Equal(1024 * 1024, o.MaxDownloadLimit);
        Assert.Equal(256 * 1024, o.MaxUploadLimit);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1024, "1K")]
    [InlineData(2 * 1024, "2K")]
    [InlineData(1024L * 1024, "1M")]
    [InlineData(10L * 1024 * 1024, "10M")]
    [InlineData(1536, "1536")]
    [InlineData(999, "999")]
    public void FormatBytes_HandlesCommonSizes(long input, string expected)
    {
        Assert.Equal(expected, Aria2Options.FormatBytes(input));
    }
}

public class ChangeOptionRpcTests
{
    [Fact]
    public async Task ChangeOptionAsync_SendsAria2ChangeOption_WithFormattedLimits()
    {
        string? body = null;
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":"ok"}""")
            };
        });
        await using var client = new Aria2JsonRpcClient(new Uri("http://127.0.0.1:6800/jsonrpc"));
        typeof(Aria2JsonRpcClient)
            .GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(client, new HttpClient(mock) { BaseAddress = new Uri("http://127.0.0.1:6800/jsonrpc") });

        var limits = new Dictionary<string, object?>
        {
            ["max-download-limit"] = Aria2Options.FormatBytes(2L * 1024 * 1024),
            ["max-upload-limit"] = Aria2Options.FormatBytes(512L * 1024)
        };
        await client.ChangeOptionAsync("gid-123", limits);

        Assert.NotNull(body);
        var parsed = JsonNode.Parse(body!)!;
        Assert.Equal("aria2.changeOption", parsed["method"]!.GetValue<string>());
        var prm = parsed["params"]!.AsArray();
        Assert.Equal("gid-123", prm[1]!.GetValue<string>());
        var opts = prm[2]!.AsObject();
        Assert.Equal("2M", opts["max-download-limit"]!.GetValue<string>());
        Assert.Equal("512K", opts["max-upload-limit"]!.GetValue<string>());
    }
}
