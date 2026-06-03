using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using o_down.Core.Abstractions;

namespace o_down.Core.Protocol;

/// <summary>
/// Canonical wire format for messages between browser extensions, the
/// native-messaging host, and the named-pipe server running inside o-down.
///
/// Browser side (Chrome/Firefox/Edge): the standard native-messaging framing
/// is a 4-byte little-endian length prefix followed by UTF-8 JSON. The host
/// EXE reads that exact framing from stdin and writes the same to stdout.
///
/// Pipe side (host → o-down.App): the same 4-byte length prefix + UTF-8 JSON
/// framing is reused so both halves can share <see cref="NativeMessageCodec"/>
/// without translation. Each request gets one response on the same pipe
/// connection; the pipe is then closed by the host.
/// </summary>
public static class NativeMessageCodec
{
    public const int MaxMessageBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static byte[] EncodeRequest(CapturedLink link)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(ToWire(link), JsonOptions);
        if (json.Length > MaxMessageBytes)
            throw new InvalidOperationException($"native message payload {json.Length} bytes exceeds {MaxMessageBytes} cap");
        return WrapWithLength(json);
    }

    public static byte[] EncodeResponse(NativeResponse response)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        return WrapWithLength(json);
    }

    public static async Task<CapturedLink?> ReadRequestAsync(Stream stream, CancellationToken ct = default)
    {
        var body = await ReadFramedJsonAsync(stream, ct).ConfigureAwait(false);
        if (body is null) return null;
        var wire = JsonSerializer.Deserialize<NativeLink>(body, JsonOptions);
        return wire is null ? null : FromWire(wire);
    }

    public static async Task<NativeResponse?> ReadResponseAsync(Stream stream, CancellationToken ct = default)
    {
        var body = await ReadFramedJsonAsync(stream, ct).ConfigureAwait(false);
        if (body is null) return null;
        return JsonSerializer.Deserialize<NativeResponse>(body, JsonOptions);
    }

    public static byte[] WrapWithLength(ReadOnlySpan<byte> body)
    {
        if (body.Length > MaxMessageBytes)
            throw new InvalidOperationException($"native message payload {body.Length} bytes exceeds {MaxMessageBytes} cap");
        var framed = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(framed.AsSpan(0, 4), body.Length);
        body.CopyTo(framed.AsSpan(4));
        return framed;
    }

    public static async Task<byte[]?> ReadFramedJsonAsync(Stream stream, CancellationToken ct = default)
    {
        var lengthBuf = new byte[4];
        if (!await ReadExactAsync(stream, lengthBuf, ct).ConfigureAwait(false))
            return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuf);
        if (length <= 0 || length > MaxMessageBytes) return null;
        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false)) return null;
        return body;
    }

    public static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct = default)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (n == 0) return total == buffer.Length;
            total += n;
        }
        return true;
    }

    private static NativeLink ToWire(CapturedLink link) => new()
    {
        Url = link.Url,
        Referrer = link.Referrer,
        Cookies = link.Cookies,
        FilenameHint = link.FilenameHint,
        Source = link.Source,
        CapturedAt = link.CapturedAt
    };

    private static CapturedLink FromWire(NativeLink wire) => new()
    {
        Url = wire.Url ?? string.Empty,
        Referrer = wire.Referrer,
        Cookies = wire.Cookies,
        FilenameHint = wire.FilenameHint,
        Source = wire.Source ?? "unknown",
        CapturedAt = wire.CapturedAt == default ? DateTimeOffset.UtcNow : wire.CapturedAt
    };

    public sealed class NativeLink
    {
        public string? Url { get; set; }
        public string? Referrer { get; set; }
        public string? Cookies { get; set; }
        public string? FilenameHint { get; set; }
        public string? Source { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
    }

    public sealed class NativeResponse
    {
        public bool Ok { get; set; }
        public Guid? DownloadId { get; set; }
        public string? Error { get; set; }
        public string? Version { get; set; }
    }
}
