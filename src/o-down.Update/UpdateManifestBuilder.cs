using System.Security.Cryptography;
using System.Text.Json;

namespace o_down.Update;

public sealed class UpdateManifestBuilder
{
    public static async Task<UpdateManifest> BuildFromZipAsync(
        string version,
        string channel,
        string zipPath,
        string downloadUrl,
        string? notes = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("version required", nameof(version));
        if (string.IsNullOrWhiteSpace(zipPath)) throw new ArgumentException("zipPath required", nameof(zipPath));
        if (!File.Exists(zipPath)) throw new FileNotFoundException("Update zip not found", zipPath);

        var fi = new FileInfo(zipPath);
        await using var src = fi.OpenRead();
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(src, ct).ConfigureAwait(false);

        return new UpdateManifest
        {
            Version = version,
            Channel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel,
            ReleaseDate = DateTimeOffset.UtcNow,
            DownloadUrl = downloadUrl,
            Sha256 = Convert.ToHexString(hash),
            SizeBytes = fi.Length,
            Notes = notes,
        };
    }

    public static async Task WriteAsync(UpdateManifest manifest, string outputPath, CancellationToken ct = default)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("outputPath required", nameof(outputPath));

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = outputPath + ".tmp";
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
        });
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        File.Move(tmp, outputPath, overwrite: true);
    }
}
