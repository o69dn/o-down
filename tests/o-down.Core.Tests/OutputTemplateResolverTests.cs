using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class OutputTemplateResolverTests
{
    [Fact]
    public void Resolve_SimpleTemplate_SubstitutesTitleAndExt()
    {
        var ctx = new MediaTemplateContext { Title = "Big Buck Bunny", Extension = "mp4", FallbackTitle = "x" };
        var path = OutputTemplateResolver.Resolve("%(title)s.%(ext)s", ctx);
        Assert.Equal("Big Buck Bunny.mp4", path);
    }

    [Fact]
    public void Resolve_TitleWithInvalidChars_Sanitizes()
    {
        var ctx = new MediaTemplateContext { Title = "Hello/World: Test?", Extension = "mp4", FallbackTitle = "x" };
        var path = OutputTemplateResolver.Resolve("%(title)s.%(ext)s", ctx);
        Assert.Equal("Hello_World_ Test_.mp4", path);
    }

    [Fact]
    public void Resolve_WithUploader_Expands()
    {
        var ctx = new MediaTemplateContext { Title = "video", Uploader = "Blender", Extension = "mp4", FallbackTitle = "x" };
        var path = OutputTemplateResolver.Resolve("%(uploader)s - %(title)s.%(ext)s", ctx);
        Assert.Equal("Blender - video.mp4", path);
    }

    [Fact]
    public void Resolve_EmptyTemplate_UsesFallbackTitle()
    {
        var ctx = new MediaTemplateContext { Extension = "mp4", FallbackTitle = "fallback" };
        var path = OutputTemplateResolver.Resolve("", ctx);
        Assert.Equal("fallback.%(ext)s", path);
    }

    [Fact]
    public void Resolve_UnknownToken_RemovesIt()
    {
        var ctx = new MediaTemplateContext { Title = "video", Extension = "mp4", FallbackTitle = "x" };
        var path = OutputTemplateResolver.Resolve("%(title)s-%(unknown)s.%(ext)s", ctx);
        Assert.Equal("video-.mp4", path);
    }

    [Fact]
    public void Resolve_TrailingDotIsStripped()
    {
        var ctx = new MediaTemplateContext { Title = "  ...weird...  ", Extension = "mp4", FallbackTitle = "x" };
        var path = OutputTemplateResolver.Resolve("%(title)s.%(ext)s", ctx);
        Assert.Equal("weird.mp4", path);
    }

    [Fact]
    public void SanitizeFileName_ReplacesAllInvalidChars()
    {
        var s = OutputTemplateResolver.SanitizeFileName("a<b>c:d/e\\f|g?h*i");
        Assert.Equal("a_b_c_d_e_f_g_h_i", s);
    }

    [Fact]
    public void SanitizeFileName_EmptyReturnsUntitiled()
    {
        Assert.Equal("untitled", OutputTemplateResolver.SanitizeFileName(""));
        Assert.Equal("untitled", OutputTemplateResolver.SanitizeFileName("   "));
        Assert.Equal("___", OutputTemplateResolver.SanitizeFileName("///"));
    }

    [Fact]
    public void Resolve_ResolutionAndFps_Expand()
    {
        var ctx = new MediaTemplateContext
        {
            Title = "video",
            Extension = "mp4",
            Resolution = "1080p",
            Fps = 60,
            FallbackTitle = "x"
        };
        var path = OutputTemplateResolver.Resolve("%(title)s-%(resolution)s@%(fps)s.%(ext)s", ctx);
        Assert.Equal("video-1080p@60.mp4", path);
    }
}
