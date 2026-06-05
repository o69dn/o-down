using Microsoft.EntityFrameworkCore;
using o_down.Core.Models;

namespace o_down.Data;

public sealed class OdownDbInitializer
{
    private readonly OdownDbContext _db;
    public OdownDbInitializer(OdownDbContext db) => _db = db;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        if (!await _db.BandwidthProfiles.AnyAsync(ct).ConfigureAwait(false))
        {
            _db.BandwidthProfiles.AddRange(BuiltInBandwidthProfiles.All);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        if (!await _db.CategoryRules.AnyAsync(ct).ConfigureAwait(false))
        {
            _db.CategoryRules.AddRange(
                new CategoryRule { Name = "Video",   ExtensionPattern = "*.mp4;*.mkv;*.mov;*.avi;*.webm;*.flv", DestinationDirectory = "%USERPROFILE%\\Videos" },
                new CategoryRule { Name = "Audio",   ExtensionPattern = "*.mp3;*.flac;*.wav;*.m4a;*.ogg;*.opus",  DestinationDirectory = "%USERPROFILE%\\Music" },
                new CategoryRule { Name = "Images",  ExtensionPattern = "*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp",DestinationDirectory = "%USERPROFILE%\\Pictures" },
                new CategoryRule { Name = "Docs",    ExtensionPattern = "*.pdf;*.docx;*.xlsx;*.pptx;*.txt;*.epub", DestinationDirectory = "%USERPROFILE%\\Documents" },
                new CategoryRule { Name = "Archives",ExtensionPattern = "*.zip;*.7z;*.rar;*.tar;*.gz;*.iso",   DestinationDirectory = "%USERPROFILE%\\Downloads\\Archives" },
                new CategoryRule { Name = "Installers",ExtensionPattern = "*.exe;*.msi;*.apk;*.dmg",            DestinationDirectory = "%USERPROFILE%\\Downloads\\Installers" }
            );
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
