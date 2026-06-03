using System.Text.RegularExpressions;

namespace o_down.Core.Pipeline;

public static class UrlClassifier
{
    private static readonly Regex MagnetRegex = new(@"^magnet:\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TorrentFileRegex = new(@"\.torrent(\?|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HttpRegex = new(@"^https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] StreamingSites =
    {
        "youtube.com", "youtu.be", "vimeo.com", "dailymotion.com", "twitch.tv",
        "twitter.com", "x.com", "facebook.com", "instagram.com", "tiktok.com",
        "reddit.com", "soundcloud.com", "bandcamp.com", "nicovideo.jp"
    };

    public static Models.DownloadKind Classify(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Models.DownloadKind.Http;
        if (MagnetRegex.IsMatch(url)) return Models.DownloadKind.Torrent;
        if (TorrentFileRegex.IsMatch(url)) return Models.DownloadKind.Torrent;
        if (HttpRegex.IsMatch(url))
        {
            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();
                if (StreamingSites.Any(s => host == s || host.EndsWith("." + s)))
                    return Models.DownloadKind.Media;
            }
            catch
            {
            }
        }
        return Models.DownloadKind.Http;
    }

    public static bool IsUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return HttpRegex.IsMatch(text) || MagnetRegex.IsMatch(text);
    }
}
