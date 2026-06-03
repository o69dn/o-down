using System.Net;
using System.Web;
using o_down.Core.Models;

namespace o_down.Core.Pipeline;

public static class MagnetLinkParser
{
    public static MagnetLinkInfo Parse(string magnet)
    {
        if (string.IsNullOrWhiteSpace(magnet))
            throw new ArgumentException("Magnet link is empty", nameof(magnet));
        if (!magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Not a magnet link: " + magnet);

        var info = new MagnetLinkInfo();
        var qs = magnet["magnet:?".Length..];
        foreach (var pair in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            string rawKey = eq < 0 ? pair : pair[..eq];
            string rawVal = eq < 0 ? string.Empty : pair[(eq + 1)..];
            string key = WebUtility.UrlDecode(rawKey);
            string val = WebUtility.UrlDecode(rawVal);

            switch (key)
            {
                case "xt":
                    if (val.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
                        info.InfoHash = val["urn:btih:".Length..].ToLowerInvariant();
                    else if (val.StartsWith("urn:btmh:", StringComparison.OrdinalIgnoreCase))
                        info.InfoHash = val["urn:btmh:".Length..].ToLowerInvariant();
                    break;
                case "dn":
                    info.DisplayName = val;
                    break;
                case "tr":
                    if (!string.IsNullOrEmpty(val)) info.Trackers.Add(val);
                    break;
                case "ws":
                    if (!string.IsNullOrEmpty(val)) info.WebSeeds.Add(val);
                    break;
                case "xs":
                    if (!string.IsNullOrEmpty(val)) info.ExactSources.Add(val);
                    break;
                case "x.pe":
                case "x.pe.":
                    if (!string.IsNullOrEmpty(val)) info.Trackers.Add("peer://" + val);
                    break;
                case "kt":
                    info.KeywordTopic = val;
                    break;
                case "as":
                    info.AcceptableSource = val;
                    break;
                case "xl":
                    if (long.TryParse(val, out var size)) info.Size = size;
                    break;
            }
        }

        return info;
    }
}
