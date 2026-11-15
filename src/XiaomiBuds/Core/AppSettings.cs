using System.Text.Json;

namespace XiaomiBuds.Core;

/// <summary>Per-device preferences (model color, custom name) stored as JSON next to the log.</summary>
public static class AppSettings
{
    sealed class Data
    {
        public Dictionary<string, string> Colors { get; set; } = new();
        public Dictionary<string, string> Names { get; set; } = new();
        public string? LastDevice { get; set; }
    }

    /// <summary>MAC of the earbuds that were on screen last (reopened on start).</summary>
    public static string? LastDevice
    {
        get { lock (Gate) return D.LastDevice; }
        set { lock (Gate) { if (D.LastDevice == value) return; D.LastDevice = value; Save(); } }
    }

    static readonly string FilePath = Path.Combine(Log.Dir, "settings.json");
    // "Xiaomi Ecosystem" was this app's earlier name; on first run carry over names, colors and last device.
    static readonly string OldFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XiaomiEcosystem", "settings.json");

    static readonly object Gate = new();
    static readonly Data D = Load();

    static Data Load()
    {
        var path = File.Exists(FilePath) ? FilePath : OldFilePath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e) { Log.Warn("settings unreadable: " + e.Message); }
        return new Data();
    }

    static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(D, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Log.Warn("settings not saved: " + e.Message); }
    }

    public static string? GetColor(string mac) { lock (Gate) return D.Colors.TryGetValue(mac, out var c) ? c : null; }

    public static void SetColor(string mac, string color) { lock (Gate) { D.Colors[mac] = color; Save(); } }

    public static string? GetName(string mac) { lock (Gate) return D.Names.TryGetValue(mac, out var n) ? n : null; }

    /// <summary>Set a custom display name; null or blank restores the Bluetooth name.</summary>
    public static void SetName(string mac, string? name)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(name)) D.Names.Remove(mac);
            else D.Names[mac] = name.Trim();
            Save();
        }
    }
}
