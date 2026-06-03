using System.Security.Cryptography;

namespace o_down.Core.Pipeline;

public static class ChecksumHelper
{
    public static bool TryParse(string input, out string algorithm, out string expected)
    {
        algorithm = string.Empty;
        expected = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.Trim();
        var idx = trimmed.IndexOf(':');
        if (idx > 0)
        {
            var alg = trimmed[..idx].ToLowerInvariant();
            var value = trimmed[(idx + 1)..];
            if (alg is "md5" or "sha1" or "sha256" or "sha384" or "sha512")
            {
                algorithm = alg;
                expected = value.ToLowerInvariant();
                return true;
            }
        }
        if (trimmed.Length == 32)
        {
            algorithm = "md5";
            expected = trimmed.ToLowerInvariant();
            return true;
        }
        if (trimmed.Length == 40)
        {
            algorithm = "sha1";
            expected = trimmed.ToLowerInvariant();
            return true;
        }
        if (trimmed.Length == 64)
        {
            algorithm = "sha256";
            expected = trimmed.ToLowerInvariant();
            return true;
        }
        return false;
    }

    public static HashAlgorithm Create(string algorithm) => algorithm switch
    {
        "md5" => MD5.Create(),
        "sha1" => SHA1.Create(),
        "sha256" => SHA256.Create(),
        "sha384" => SHA384.Create(),
        "sha512" => SHA512.Create(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown algorithm")
    };
}
