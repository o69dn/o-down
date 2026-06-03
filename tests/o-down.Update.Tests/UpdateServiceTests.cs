using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using o_down.Update;
using Xunit;

namespace o_down.Update.Tests;

public class UpdateServiceTests : IDisposable
{
    private readonly string _workDir;
    public UpdateServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "odown-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }
    public void Dispose()
    {
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Fact]
    public void IsNewer_ReturnsTrue_When_RemoteGreater()
    {
        Assert.True(UpdateService.IsNewer("1.2.0", "1.1.5"));
        Assert.True(UpdateService.IsNewer("2.0.0", "1.99.99"));
    }

    [Fact]
    public void IsNewer_ReturnsFalse_When_RemoteEqual()
    {
        Assert.False(UpdateService.IsNewer("1.0.0", "1.0.0"));
    }

    [Fact]
    public void IsNewer_ReturnsFalse_When_RemoteLesser()
    {
        Assert.False(UpdateService.IsNewer("0.9.0", "1.0.0"));
        Assert.False(UpdateService.IsNewer("1.0.0", "1.0.1"));
    }

    [Fact]
    public void IsNewer_ReturnsFalse_When_RemoteInvalid()
    {
        Assert.False(UpdateService.IsNewer("not-a-version", "1.0.0"));
        Assert.False(UpdateService.IsNewer("", "1.0.0"));
        Assert.False(UpdateService.IsNewer(null, "1.0.0"));
    }

    [Fact]
    public void IsNewer_ReturnsTrue_When_CurrentInvalid()
    {
        Assert.True(UpdateService.IsNewer("1.0.0", "garbage"));
        Assert.True(UpdateService.IsNewer("1.0.0", ""));
        Assert.True(UpdateService.IsNewer("1.0.0", null));
    }

    [Fact]
    public async Task VerifySha256Async_ReturnsTrue_When_HashMatches()
    {
        var data = Encoding.UTF8.GetBytes("o-down payload " + new string('x', 1000));
        var path = Path.Combine(_workDir, "data.bin");
        File.WriteAllBytes(path, data);
        var expected = Convert.ToHexString(SHA256.HashData(data));

        var svc = NewService();
        Assert.True(await svc.VerifySha256Async(path, expected));
    }

    [Fact]
    public async Task VerifySha256Async_ReturnsFalse_When_HashMismatches()
    {
        var path = Path.Combine(_workDir, "data.bin");
        File.WriteAllBytes(path, "hello"u8.ToArray());
        var svc = NewService();
        Assert.False(await svc.VerifySha256Async(path, new string('0', 64)));
    }

    [Fact]
    public async Task VerifySha256Async_AcceptsLowercaseAndFormattedHex()
    {
        var data = "payload"u8.ToArray();
        var path = Path.Combine(_workDir, "data.bin");
        File.WriteAllBytes(path, data);
        var expected = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var withDashes = string.Join("-", Enumerable.Range(0, expected.Length / 2).Select(i => expected.Substring(i * 2, 2)));

        var svc = NewService();
        Assert.True(await svc.VerifySha256Async(path, withDashes));
    }

    [Fact]
    public async Task VerifySha256Async_Skips_When_ExpectedEmpty()
    {
        var path = Path.Combine(_workDir, "data.bin");
        File.WriteAllBytes(path, "anything"u8.ToArray());
        var svc = NewService();
        Assert.True(await svc.VerifySha256Async(path, ""));
        Assert.True(await svc.VerifySha256Async(path, "   "));
    }

    [Fact]
    public async Task StageAsync_ExtractsZipContents()
    {
        var (zipPath, _) = MakeUpdateZip("v1.0.0", exeContents: "v1");

        var staging = Path.Combine(_workDir, "staging");
        var svc = NewService();
        await svc.StageAsync(zipPath, staging);

        Assert.True(File.Exists(Path.Combine(staging, "o-down.App.exe")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(staging, "o-down.App.exe")));
    }

    [Fact]
    public async Task StageAsync_OverwritesExistingStagingDir()
    {
        var (zipPath, _) = MakeUpdateZip("v1.0.0", exeContents: "fresh");
        var staging = Path.Combine(_workDir, "staging");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "stale.txt"), "stale data");

        var svc = NewService();
        await svc.StageAsync(zipPath, staging);

        Assert.False(File.Exists(Path.Combine(staging, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(staging, "o-down.App.exe")));
    }

    [Fact]
    public async Task ApplyAsync_ReplacesAppDirContents()
    {
        var (zipPath, appDir) = MakeUpdateZip("v1.0.0", exeContents: "fresh");
        var oldExePath = Path.Combine(appDir, "o-down.App.exe");
        File.WriteAllText(oldExePath, "OLD-CONTENTS");
        File.WriteAllText(Path.Combine(appDir, "user-config.json"), "user data that survives");

        var svc = NewService(appDir);
        var newExe = await svc.ApplyAsync(zipPath, oldExePath);

        Assert.True(File.Exists(newExe));
        Assert.Equal("fresh", File.ReadAllText(newExe));
        Assert.NotEqual("OLD-CONTENTS", File.ReadAllText(newExe));
        Assert.Equal(Path.Combine(appDir, "o-down.App.exe"), newExe);
    }

    [Fact]
    public async Task ApplyAsync_DeletesBackupAndStaging_OnSuccess()
    {
        var (zipPath, appDir) = MakeUpdateZip("v1.0.0", exeContents: "fresh");
        var oldExe = Path.Combine(appDir, "o-down.App.exe");

        var svc = NewService(appDir);
        await svc.ApplyAsync(zipPath, oldExe);

        var parent = Path.GetDirectoryName(appDir)!;
        Assert.DoesNotContain(Directory.GetDirectories(parent), d => Path.GetFileName(d).Contains(".old-"));
        Assert.DoesNotContain(Directory.GetDirectories(parent), d => Path.GetFileName(d).Contains(".update-staging-"));
    }

    [Fact]
    public async Task ApplyAsync_ThrowsForNonExistentExePath()
    {
        var (zipPath, appDir) = MakeUpdateZip("v1.0.0", exeContents: "fresh");
        var missing = Path.Combine(appDir, "does-not-exist.exe");
        var svc = NewService(appDir);

        await Assert.ThrowsAsync<FileNotFoundException>(async () => await svc.ApplyAsync(zipPath, missing));
    }

    [Fact]
    public async Task CheckAsync_ReturnsHasUpdateTrue_When_RemoteIsNewer()
    {
        var manifest = new UpdateManifest { Version = "99.0.0", DownloadUrl = "https://example.com", Sha256 = "" };
        var feed = new StubFeed(manifest);
        var svc = new UpdateService(feed, _workDir, new Version(1, 0, 0));

        var result = await svc.CheckAsync("stable");
        Assert.True(result.HasUpdate);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Equal("99.0.0", result.Manifest!.Version);
    }

    [Fact]
    public async Task CheckAsync_ReturnsHasUpdateFalse_When_RemoteIsEqual()
    {
        var feed = new StubFeed(new UpdateManifest { Version = "1.0.0", DownloadUrl = "x", Sha256 = "" });
        var svc = new UpdateService(feed, _workDir, new Version(1, 0, 0));
        var result = await svc.CheckAsync("stable");
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task CheckAsync_ReturnsError_When_ManifestUnreachable()
    {
        var feed = new StubFeed(null);
        var svc = new UpdateService(feed, _workDir, new Version(1, 0, 0));
        var result = await svc.CheckAsync("stable");
        Assert.False(result.HasUpdate);
        Assert.Equal("No manifest", result.Error);
    }

    private UpdateService NewService(string? appDir = null)
        => new(new StubFeed(null), appDir ?? _workDir, new Version(1, 0, 0));

    private (string ZipPath, string AppDir) MakeUpdateZip(string version, string exeContents)
    {
        var appDir = Path.Combine(_workDir, "app-" + version);
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "o-down.App.exe"), exeContents);
        File.WriteAllText(Path.Combine(appDir, "sidecar.txt"), "sidecar data");

        var zipPath = Path.Combine(_workDir, $"o-down-{version}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(appDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return (zipPath, appDir);
    }

    private sealed class StubFeed : IUpdateFeed
    {
        private readonly UpdateManifest? _manifest;
        public StubFeed(UpdateManifest? manifest) => _manifest = manifest;
        public Task<UpdateManifest?> GetLatestAsync(string channel, CancellationToken ct = default)
            => Task.FromResult(_manifest);
    }
}
