using System.Text.Json;

namespace MBExtreme.Core.Tweaks;

/// <summary>
/// Guarda os valores originais de cada ajuste antes de alterá-lo, para poder restaurar.
/// Arquivo: %AppData%\MBExtreme\tweak-backup.json.
/// </summary>
public sealed class TweakStore
{
    private readonly object _lock = new();
    private readonly string _path;
    private readonly Dictionary<string, string> _data;

    public TweakStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _data = Load(_path);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "tweak-backup.json");

    public bool Has(string key)
    {
        lock (_lock) return _data.ContainsKey(key);
    }

    public string? Get(string key)
    {
        lock (_lock) return _data.GetValueOrDefault(key);
    }

    public void Set(string key, string value)
    {
        lock (_lock)
        {
            _data[key] = value;
            Save();
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            if (_data.Remove(key)) Save();
        }
    }

    public bool HasPrefix(string prefix)
    {
        lock (_lock) return _data.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Chaves guardadas que começam com o prefixo (cópia, para poder remover enquanto percorre).</summary>
    public IReadOnlyList<string> KeysWithPrefix(string prefix)
    {
        lock (_lock) return _data.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Arquivo ilegível: começar vazio.
        }

        return new Dictionary<string, string>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Não foi possível gravar o backup dos valores originais. O ajuste não foi aplicado.", ex);
        }
    }
}
