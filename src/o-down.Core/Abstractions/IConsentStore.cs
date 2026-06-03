using System.Text.Json;

namespace o_down.Core.Abstractions;

/// <summary>
/// Records per-feature opt-ins (clipboard monitoring, etc.).
/// Default behavior is "no consent" — a feature is disabled until the user
/// explicitly enables it via the Settings page. The settings UI is
/// responsible for writing these flags; this class is read-only on the
/// hot path so it can be queried from the App startup without blocking
/// on the UI thread.
/// </summary>
public interface IConsentStore
{
    bool IsEnabled(string feature);
    void SetEnabled(string feature, bool enabled);
    IReadOnlyDictionary<string, bool> Snapshot();
}

public sealed class FileConsentStore : IConsentStore
{
    public const string ClipboardFeature = "clipboard";
    public const string BrowserExtensionFeature = "browser-extension";

    private readonly string _filePath;
    private readonly Dictionary<string, bool> _state;
    private readonly object _lock = new();

    public FileConsentStore(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "consent.json");
        _state = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        TryLoad();
    }

    public bool IsEnabled(string feature)
    {
        lock (_lock) return _state.TryGetValue(feature, out var v) && v;
    }

    public void SetEnabled(string feature, bool enabled)
    {
        lock (_lock)
        {
            _state[feature] = enabled;
            Persist();
        }
    }

    public IReadOnlyDictionary<string, bool> Snapshot()
    {
        lock (_lock) return new Dictionary<string, bool>(_state, StringComparer.OrdinalIgnoreCase);
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (parsed is null) return;
            foreach (var (k, v) in parsed) _state[k] = v;
        }
        catch
        {
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_state));
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch
        {
        }
    }
}
