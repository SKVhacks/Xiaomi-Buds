using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace XiaomiBuds.Core;

public sealed record PairedDevice(string Name, ulong Address)
{
    public string Mac => string.Join(":", Enumerable.Range(0, 6).Select(i => ((Address >> (40 - 8 * i)) & 0xFF).ToString("X2")));
}

/// <summary>Watches Windows' paired Bluetooth devices and reports the list whenever it changes.</summary>
public sealed class DeviceDiscovery : IDisposable
{
    public event Action<IReadOnlyList<PairedDevice>>? Changed;

    DeviceWatcher? _watcher;
    readonly Timer _debounce;
    string _lastKey = "\0";

    public DeviceDiscovery() => _debounce = new Timer(_ => _ = RefreshAsync());

    public void Start()
    {
        _watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        _watcher.Added += (_, _) => Kick();
        _watcher.Removed += (_, _) => Kick();
        _watcher.EnumerationCompleted += (_, _) => Kick();
        _watcher.Updated += (_, _) => { };
        _watcher.Start();
        Kick();
    }

    void Kick() => _debounce.Change(300, Timeout.Infinite);

    async Task RefreshAsync()
    {
        try
        {
            var list = await ListAsync();
            var key = string.Join("|", list.Select(d => d.Mac + d.Name));
            if (key == _lastKey) return;
            _lastKey = key;
            Log.Info("paired: " + (list.Count == 0 ? "(none)" : string.Join(", ", list.Select(d => $"{d.Name} [{d.Mac}]"))));
            Changed?.Invoke(list);
        }
        catch (Exception e) { Log.Error("device enumeration failed", e); }
    }

    public static async Task<List<PairedDevice>> ListAsync()
    {
        var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        var found = new Dictionary<ulong, PairedDevice>();
        foreach (var info in infos)
        {
            try
            {
                using var dev = await BluetoothDevice.FromIdAsync(info.Id);
                if (dev is null) continue;
                found[dev.BluetoothAddress] = new PairedDevice(string.IsNullOrEmpty(info.Name) ? dev.Name : info.Name, dev.BluetoothAddress);
            }
            catch { }
        }
        return found.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch { }
        _debounce.Dispose();
    }
}
