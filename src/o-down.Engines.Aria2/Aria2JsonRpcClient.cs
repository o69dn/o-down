using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using Microsoft.Extensions.Logging;

namespace o_down.Engines.Aria2;

public sealed class Aria2JsonRpcClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _secret;
    private int _id;

    public Aria2JsonRpcClient(Uri endpoint, string? secret = null)
    {
        _endpoint = endpoint;
        _secret = secret ?? string.Empty;
        _http = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(30) };
    }

    public Task<JsonNode?> CallAsync(string method, CancellationToken ct = default) => CallAsync(method, Array.Empty<object?>(), ct);
    public Task<JsonNode?> CallAsync(string method, object? arg0, CancellationToken ct = default) => CallAsync(method, new object?[] { arg0 }, ct);
    public Task<JsonNode?> CallAsync(string method, object? arg0, object? arg1, CancellationToken ct = default) => CallAsync(method, new object?[] { arg0, arg1 }, ct);
    public Task<JsonNode?> CallAsync(string method, object? arg0, object? arg1, object? arg2, CancellationToken ct = default) => CallAsync(method, new object?[] { arg0, arg1, arg2 }, ct);
    public Task<JsonNode?> CallAsync(string method, object? arg0, object? arg1, object? arg2, object? arg3, CancellationToken ct = default) => CallAsync(method, new object?[] { arg0, arg1, arg2, arg3 }, ct);

    public async Task<JsonNode?> CallAsync(string method, object?[] args, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _id);
        var parameters = new JsonArray { $"token:{_secret}" };
        foreach (var a in args) parameters.Add(ToJsonNode(a));
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };
        var body = payload.ToJsonString();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("", content, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"aria2 RPC {method} failed: {resp.StatusCode} body={err}");
        }
        var node = await resp.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct).ConfigureAwait(false);
        if (node is null) return null;
        if (node["error"] is JsonNode errNode)
            throw new InvalidOperationException($"aria2 RPC error: {errNode}");
        return node["result"];
    }

    public async Task<IReadOnlyList<JsonNode?>> MultiCallAsync(IReadOnlyList<(string method, object?[] args)> calls, CancellationToken ct = default)
    {
        if (calls.Count == 0) return Array.Empty<JsonNode?>();
        var id = Interlocked.Increment(ref _id);
        var multiParams = new JsonArray();
        foreach (var (method, args) in calls)
        {
            var oneParams = new JsonArray { $"token:{_secret}" };
            foreach (var a in args) oneParams.Add(ToJsonNode(a));
            multiParams.Add(new JsonObject
            {
                ["methodName"] = method,
                ["params"] = oneParams
            });
        }
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "system.multicall",
            ["params"] = new JsonArray { multiParams }
        };
        var body = payload.ToJsonString();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("", content, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"aria2 multiCall failed: {resp.StatusCode} body={err}");
        }
        var node = await resp.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct).ConfigureAwait(false);
        if (node is null) return Array.Empty<JsonNode?>();
        if (node["error"] is JsonNode errNode)
            throw new InvalidOperationException($"aria2 multiCall error: {errNode}");
        var results = new List<JsonNode?>(calls.Count);
        if (node["result"] is JsonArray batch)
        {
            foreach (var entry in batch)
            {
                if (entry is JsonArray pair && pair.Count >= 1)
                    results.Add(pair[0]);
                else
                    results.Add(null);
            }
        }
        return results;
    }

    public Task<JsonNode?> TellStatusAsync(string gid, string[]? keys = null, CancellationToken ct = default) =>
        CallAsync("aria2.tellStatus", new object[] { gid, keys ?? Array.Empty<string>() }, ct);

    public Task<JsonNode?> GetOptionAsync(string gid, CancellationToken ct = default) =>
        CallAsync("aria2.getOption", gid, ct);

    public Task<JsonNode?> ChangeOptionAsync(string gid, IReadOnlyDictionary<string, object?> options, CancellationToken ct = default) =>
        CallAsync("aria2.changeOption", new object[] { gid, options }, ct);

    public Task<JsonNode?> TellStoppedAsync(int offset, int num, string[]? keys = null, CancellationToken ct = default) =>
        CallAsync("aria2.tellStopped", new object[] { offset, num, keys ?? Array.Empty<string>() }, ct);

    public Task<JsonNode?> TellWaitingAsync(int offset, int num, string[]? keys = null, CancellationToken ct = default) =>
        CallAsync("aria2.tellWaiting", new object[] { offset, num, keys ?? Array.Empty<string>() }, ct);

    public Task<JsonNode?> RemoveDownloadResultAsync(string gid, CancellationToken ct = default) =>
        CallAsync("aria2.removeDownloadResult", gid, ct);

    public Task<JsonNode?> GetFilesAsync(string gid, CancellationToken ct = default) =>
        CallAsync("aria2.getFiles", gid, ct);

    public Task<JsonNode?> GetGlobalOptionAsync(CancellationToken ct = default) =>
        CallAsync("aria2.getGlobalOption", Array.Empty<object?>(), ct);

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        JsonNode n => n,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        decimal m => JsonValue.Create(m),
        DateTime dt => JsonValue.Create(dt),
        DateTimeOffset dto => JsonValue.Create(dto),
        Guid g => JsonValue.Create(g),
        _ => JsonSerializer.SerializeToNode(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        })
    };

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
