using System.Text.Json;

namespace GamesirBatteryTray;

/// <summary>A controller known to the app, identified by the MAC reported by its receiver.</summary>
internal sealed class DeviceEntry
{
    public string Mac { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Persists display names so each controller keeps its label across restarts.</summary>
internal sealed class DeviceRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configPath;
    private readonly List<DeviceEntry> _entries = new();

    private DeviceRegistry(string configPath) => _configPath = configPath;

    public IReadOnlyList<DeviceEntry> Entries => _entries;

    public static DeviceRegistry Load()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameSirBatteryTray");
        var path = Path.Combine(directory, "devices.json");
        var registry = new DeviceRegistry(path);

        try
        {
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<List<DeviceEntry>>(File.ReadAllText(path), JsonOptions);
                if (saved is not null)
                {
                    foreach (var entry in saved.Where(e => !string.IsNullOrWhiteSpace(e.Mac)))
                        registry._entries.Add(new DeviceEntry
                        {
                            Mac = entry.Mac.ToUpperInvariant(),
                            Name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Mac : entry.Name
                        });
                }
            }
        }
        catch
        {
            // A damaged config must never block startup; fall back to defaults.
        }

        return registry;
    }

    public int IndexOf(string mac)
    {
        var index = _entries.FindIndex(e => string.Equals(e.Mac, mac, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? _entries.Count : index;
    }

    public string ResolveName(string mac)
    {
        var existing = _entries.FirstOrDefault(e => string.Equals(e.Mac, mac, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing.Name;

        var name = $"手柄 {_entries.Count + 1}";
        _entries.Add(new DeviceEntry { Mac = mac.ToUpperInvariant(), Name = name });
        Save();
        return name;
    }

    public void Rename(string mac, string name)
    {
        var entry = _entries.FirstOrDefault(e => string.Equals(e.Mac, mac, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            _entries.Add(new DeviceEntry { Mac = mac.ToUpperInvariant(), Name = name });
        }
        else
        {
            entry.Name = name;
        }

        Save();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(_entries, JsonOptions));
        }
        catch
        {
            // Persisting is best-effort; names still work for the current session.
        }
    }
}
