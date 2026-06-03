using System.Text.Json;
using o_down.Core.Models;

namespace o_down.Core.Pipeline;

public sealed class AutoSortEngine
{
    private readonly IReadOnlyList<CategoryRule> _rules;

    public AutoSortEngine(IEnumerable<CategoryRule> rules) =>
        _rules = rules.OrderByDescending(r => r.Priority).ToList();

    public CategoryRule? Match(DownloadItem item)
    {
        var filename = item.FilenameHint;
        if (string.IsNullOrEmpty(filename)) return null;
        foreach (var rule in _rules.Where(r => r.IsEnabled))
        {
            if (MatchPattern(rule.ExtensionPattern, filename)) return rule;
            if (!string.IsNullOrEmpty(rule.NameRegex) &&
                System.Text.RegularExpressions.Regex.IsMatch(filename, rule.NameRegex))
                return rule;
        }
        return null;
    }

    private static bool MatchPattern(string? pattern, string filename)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        foreach (var token in pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = token.TrimStart('*', '.');
            if (filename.EndsWith(t, StringComparison.OrdinalIgnoreCase)) return true;
            if (token.StartsWith("*.") && filename.EndsWith(token[1..], StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
