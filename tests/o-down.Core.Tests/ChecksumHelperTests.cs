using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class ChecksumHelperTests
{
    [Theory]
    [InlineData("d41d8cd98f00b204e9800998ecf8427e", "md5", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("da39a3ee5e6b4b0d3255bfef95601890afd80709", "sha1", "da39a3ee5e6b4b0d3255bfef95601890afd80709")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "sha256", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("md5:d41d8cd98f00b204e9800998ecf8427e", "md5", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("sha256:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", "sha256", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void TryParse_RecognisesCommonForms(string input, string expectedAlg, string expectedValue)
    {
        Assert.True(ChecksumHelper.TryParse(input, out var alg, out var value));
        Assert.Equal(expectedAlg, alg);
        Assert.Equal(expectedValue, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("xyz:abc")]
    public void TryParse_RejectsInvalid(string input)
    {
        Assert.False(ChecksumHelper.TryParse(input, out _, out _));
    }

    [Fact]
    public void Hash_RoundtripsForKnownInput()
    {
        using var sha = ChecksumHelper.Create("sha256");
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("abc"));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hex);
    }
}
