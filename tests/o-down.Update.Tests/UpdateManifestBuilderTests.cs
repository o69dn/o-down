using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using o_down.Update;
using Xunit;

namespace o_down.Update.Tests;

public class UpdateManifestBuilderTests : IDisposable
{
    private readonly string _workDir;
    public UpdateManifestBuilderTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "odown-manifest-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }
    public void Dispose()
    {
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task BuildFromZipAsync_ComputesSha256AndSize()
    {
        var payload = new byte[1024 * 32];
        RandomNumberGenerator.Fill(payload);
        var zipPath = Path.Combine(_workDir, "o-down.zip");
        await File.WriteAllBytesAsync(zipPath, payload);

        var manifest = await UpdateManifestBuilder.BuildFromZipAsync(
            version: "1.2.0",
            channel: "stable",
            zipPath: zipPath,
            downloadUrl: "https://updates.example.com/o-down-1.2.0.zip");

        var expectedHash = Convert.ToHexString(SHA256.HashData(payload));
        Assert.Equal(expectedHash, manifest.Sha256);
        Assert.Equal(payload.Length, manifest.SizeBytes);
        Assert.Equal("1.2.0", manifest.Version);
        Assert.Equal("stable", manifest.Channel);
    }

    [Fact]
    public async Task BuildFromZipAsync_DefaultsChannel_To_Stable_WhenBlank()
    {
        var zipPath = Path.Combine(_workDir, "o-down.zip");
        await File.WriteAllBytesAsync(zipPath, "x"u8.ToArray());

        var manifest = await UpdateManifestBuilder.BuildFromZipAsync("1.0.0", " ", zipPath, "https://x");
        Assert.Equal("stable", manifest.Channel);
    }

    [Fact]
    public async Task BuildFromZipAsync_DefaultsReleaseDate_ToNow()
    {
        var zipPath = Path.Combine(_workDir, "o-down.zip");
        await File.WriteAllBytesAsync(zipPath, "x"u8.ToArray());

        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        var manifest = await UpdateManifestBuilder.BuildFromZipAsync("1.0.0", "stable", zipPath, "https://x");
        var after = DateTimeOffset.UtcNow.AddSeconds(2);

        Assert.InRange(manifest.ReleaseDate, before, after);
    }

    [Fact]
    public async Task BuildFromZipAsync_ThrowsFor_MissingZip()
    {
        var zipPath = Path.Combine(_workDir, "no-such.zip");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            UpdateManifestBuilder.BuildFromZipAsync("1.0.0", "stable", zipPath, "https://x"));
    }

    [Fact]
    public async Task BuildFromZipAsync_RejectsBlankVersion()
    {
        var zipPath = Path.Combine(_workDir, "o-down.zip");
        await File.WriteAllBytesAsync(zipPath, "x"u8.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            UpdateManifestBuilder.BuildFromZipAsync("", "stable", zipPath, "https://x"));
    }

    [Fact]
    public async Task WriteAsync_CreatesParentDirectoryIfMissing()
    {
        var nested = Path.Combine(_workDir, "a", "b", "latest.json");
        var manifest = new UpdateManifest
        {
            Version = "1.0.0",
            Channel = "stable",
            DownloadUrl = "https://x",
            Sha256 = new string('0', 64),
            SizeBytes = 12345,
            ReleaseDate = DateTimeOffset.UtcNow,
        };
        await UpdateManifestBuilder.WriteAsync(manifest, nested);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingFile_Atomically()
    {
        var outPath = Path.Combine(_workDir, "latest.json");
        var first = new UpdateManifest { Version = "0.9.0", Channel = "stable", DownloadUrl = "https://x", Sha256 = "x", SizeBytes = 1, ReleaseDate = DateTimeOffset.UtcNow };
        var second = new UpdateManifest { Version = "1.0.0", Channel = "beta", DownloadUrl = "https://y", Sha256 = new string('a', 64), SizeBytes = 2, ReleaseDate = DateTimeOffset.UtcNow };

        await UpdateManifestBuilder.WriteAsync(first, outPath);
        await UpdateManifestBuilder.WriteAsync(second, outPath);
        Assert.False(File.Exists(outPath + ".tmp"));

        var json = await File.ReadAllTextAsync(outPath);
        var roundTripped = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(roundTripped);
        Assert.Equal("1.0.0", roundTripped!.Version);
        Assert.Equal("beta", roundTripped.Channel);
    }

    [Fact]
    public async Task WriteAsync_ProducesReadableJson_WithVersionAndSha()
    {
        var outPath = Path.Combine(_workDir, "latest.json");
        var manifest = new UpdateManifest
        {
            Version = "2.5.1",
            Channel = "insider",
            DownloadUrl = "https://updates.example.com/o-down-2.5.1.zip",
            Sha256 = "DEADBEEFCAFEBABE0123456789ABCDEFFEDCBA9876543210ABCDEFFEDCBA9876",
            SizeBytes = 9999,
            Notes = "Test release",
            ReleaseDate = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
        };
        await UpdateManifestBuilder.WriteAsync(manifest, outPath);

        var json = await File.ReadAllTextAsync(outPath);
        Assert.Contains("\"version\": \"2.5.1\"", json);
        Assert.Contains("\"channel\": \"insider\"", json);
        Assert.Contains("DEADBEEFCAFEBABE0123456789ABCDEFFEDCBA9876543210ABCDEFFEDCBA9876", json);
    }

    [Fact]
    public async Task RoundTrip_BuildThenWriteThenRead()
    {
        var appDir = Path.Combine(_workDir, "fake-app");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "o-down.exe"), "binary");
        var zipPath = Path.Combine(_workDir, "o-down.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(appDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        var manifest = await UpdateManifestBuilder.BuildFromZipAsync(
            "3.0.0", "stable", zipPath, "https://updates.example.com/o-down-3.0.0.zip", "Real release");
        var outPath = Path.Combine(_workDir, "latest.json");
        await UpdateManifestBuilder.WriteAsync(manifest, outPath);

        var json = await File.ReadAllTextAsync(outPath);
        var rt = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal("3.0.0", rt!.Version);
        Assert.Equal(manifest.Sha256, rt.Sha256);
        Assert.Equal(manifest.SizeBytes, rt.SizeBytes);
        Assert.Equal("Real release", rt.Notes);
    }
}
