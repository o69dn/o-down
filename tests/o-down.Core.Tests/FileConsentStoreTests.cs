using o_down.Core.Abstractions;
using Xunit;

namespace o_down.Core.Tests;

public class FileConsentStoreTests : IDisposable
{
    private readonly string _dir;

    public FileConsentStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "odown-consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void DefaultIsDisabled()
    {
        var s = new FileConsentStore(_dir);
        Assert.False(s.IsEnabled(FileConsentStore.ClipboardFeature));
        Assert.False(s.IsEnabled("anything"));
    }

    [Fact]
    public void SetEnabled_UpdatesState()
    {
        var s = new FileConsentStore(_dir);
        s.SetEnabled(FileConsentStore.ClipboardFeature, true);
        Assert.True(s.IsEnabled(FileConsentStore.ClipboardFeature));
    }

    [Fact]
    public void StatePersistsAcrossInstances()
    {
        var a = new FileConsentStore(_dir);
        a.SetEnabled(FileConsentStore.ClipboardFeature, true);
        var b = new FileConsentStore(_dir);
        Assert.True(b.IsEnabled(FileConsentStore.ClipboardFeature));
    }

    [Fact]
    public void Disable_RemovesConsent()
    {
        var s = new FileConsentStore(_dir);
        s.SetEnabled(FileConsentStore.ClipboardFeature, true);
        s.SetEnabled(FileConsentStore.ClipboardFeature, false);
        Assert.False(s.IsEnabled(FileConsentStore.ClipboardFeature));
    }

    [Fact]
    public void Snapshot_ReflectsAllFeatures()
    {
        var s = new FileConsentStore(_dir);
        s.SetEnabled("a", true);
        s.SetEnabled("b", false);
        var snap = s.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.True(snap["a"]);
        Assert.False(snap["b"]);
    }

    [Fact]
    public void MissingDirectory_CreatesOnSet()
    {
        var nested = Path.Combine(_dir, "nested", "deeper");
        var s = new FileConsentStore(nested);
        s.SetEnabled("x", true);
        Assert.True(File.Exists(Path.Combine(nested, "consent.json")));
    }
}
