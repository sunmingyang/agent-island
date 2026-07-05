using System.IO;
using System.Text.Json;

namespace AgentIsland.Core;

/// UserDefaults equivalent: one JSON file of keyed values under
/// %APPDATA%\AgentIsland. Writes are atomic (temp + rename) so a crash
/// mid-save never truncates every preference at once.
public static class Preferences
{
    private static readonly object Gate = new();
    private static Dictionary<string, JsonElement>? _values;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static T? Get<T>(string key)
    {
        lock (Gate)
        {
            Load();
            if (_values!.TryGetValue(key, out var element))
            {
                try { return element.Deserialize<T>(); }
                catch { return default; }
            }
            return default;
        }
    }

    public static bool Has(string key)
    {
        lock (Gate)
        {
            Load();
            return _values!.ContainsKey(key);
        }
    }

    public static void Set<T>(string key, T value)
    {
        lock (Gate)
        {
            Load();
            _values![key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    public static void Remove(string key)
    {
        lock (Gate)
        {
            Load();
            if (_values!.Remove(key)) Save();
        }
    }

    private static void Load()
    {
        if (_values is not null) return;
        try
        {
            var path = IslandPaths.SettingsFile;
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                _values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text)
                    ?? new Dictionary<string, JsonElement>();
                return;
            }
        }
        catch
        {
        }
        _values = new Dictionary<string, JsonElement>();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(IslandPaths.AppSupportDir);
            var path = IslandPaths.SettingsFile;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_values, SerializerOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // A failed save costs one preference write, not the app.
        }
    }
}
