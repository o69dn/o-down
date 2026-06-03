using System.Text.Json;
using o_down.Core.Models;

namespace o_down.Core.Pipeline;

public interface IAppSettingsStore
{
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
    void Reload();
}

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private AppSettings _current = AppSettings.Default();

    public JsonAppSettingsStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public AppSettings Current => _current;

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                _current = AppSettings.Default();
                return _current;
            }
            try
            {
                await using var fs = File.OpenRead(_path);
                var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(fs, JsonOptions, ct).ConfigureAwait(false);
                _current = loaded ?? AppSettings.Default();
            }
            catch (JsonException)
            {
                _current = AppSettings.Default();
            }
            return _current;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            await using (var fs = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(fs, settings, JsonOptions, ct).ConfigureAwait(false);
            }
            File.Move(tmp, _path, overwrite: true);
            _current = settings;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Reload()
    {
        _ioLock.Wait();
        try
        {
            _current = AppSettings.Default();
            if (!File.Exists(_path)) return;
            try
            {
                using var fs = File.OpenRead(_path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(fs, JsonOptions);
                _current = loaded ?? AppSettings.Default();
            }
            catch (JsonException) { _current = AppSettings.Default(); }
        }
        finally { _ioLock.Release(); }
    }
}
