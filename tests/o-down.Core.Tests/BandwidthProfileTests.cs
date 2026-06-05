using o_down.Core.Models;
using Xunit;

namespace o_down.Core.Tests;

public class BandwidthProfileTests
{
    [Fact]
    public void Unlimited_Profile_HasNullLimits_AndUnlimitedDisplay()
    {
        var p = BuiltInBandwidthProfiles.Unlimited;
        Assert.Null(p.MaxDownloadBytesPerSecond);
        Assert.Null(p.MaxUploadBytesPerSecond);
        Assert.True(p.IsUnlimited);
        Assert.Equal("Unlimited", p.DisplaySpeed);
    }

    [Fact]
    public void LightProfile_DisplaysAsMb()
    {
        var p = BuiltInBandwidthProfiles.Light1MB;
        Assert.Equal(1024L * 1024, p.MaxDownloadBytesPerSecond);
        Assert.Equal(512L * 1024, p.MaxUploadBytesPerSecond);
        Assert.False(p.IsUnlimited);
        Assert.Contains("1 MB/s", p.DisplaySpeed);
        Assert.Contains("512 KB/s", p.DisplaySpeed);
    }

    [Fact]
    public void MediumAndFast_Profiles_OrderedBySortOrder()
    {
        var all = BuiltInBandwidthProfiles.All;
        Assert.Equal(4, all.Count);
        Assert.Equal("Unlimited", all[0].Name);
        Assert.Equal("1 MB/s (Cap)", all[1].Name);
        Assert.Equal("5 MB/s (Cap)", all[2].Name);
        Assert.Equal("10 MB/s (Cap)", all[3].Name);
    }

    [Fact]
    public void BuiltIn_Profiles_AreFlagged_AndHaveStableIds()
    {
        foreach (var p in BuiltInBandwidthProfiles.All)
        {
            Assert.True(p.IsBuiltIn);
            Assert.NotEqual(Guid.Empty, p.Id);
        }
    }

    [Fact]
    public void SameSpeed_DlAndUl_DisplayedOnce()
    {
        var p = new BandwidthProfile
        {
            MaxDownloadBytesPerSecond = 1024L * 1024,
            MaxUploadBytesPerSecond = 1024L * 1024
        };
        Assert.Equal("1 MB/s", p.DisplaySpeed);
    }

    [Fact]
    public void DifferentSpeeds_DlAndUl_ShownWithArrows()
    {
        var p = new BandwidthProfile
        {
            MaxDownloadBytesPerSecond = 10L * 1024 * 1024,
            MaxUploadBytesPerSecond = 1L * 1024 * 1024
        };
        Assert.Contains("↓10 MB/s", p.DisplaySpeed);
        Assert.Contains("↑1 MB/s", p.DisplaySpeed);
    }

    [Fact]
    public void TinySpeed_DisplayedInBytes()
    {
        var p = new BandwidthProfile { MaxDownloadBytesPerSecond = 512 };
        Assert.Equal("↓512 B/s ↑∞", p.DisplaySpeed);
    }
}
