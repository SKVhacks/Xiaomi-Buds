using Windows.Devices.Bluetooth;
using XiaomiBuds.Core;

namespace XiaomiBuds.Devices.RedmiBuds;

public enum LinkStatus { Idle, Connecting, Connected, Busy, Error }

/// <summary>
/// Keeps one pair of buds connected: connects when Windows reports the buds' audio link is up,
/// retries with backoff, exposes state and commands. Events fire on background threads.
/// </summary>
public sealed class BudsController
{
    public PairedDevice Device { get; }
    public BudsModel Model { get; }
    /// <summary>Features to show right now (experimental models depend on what the buds reported).</summary>
    public BudsModel Features => Model.Resolve(State);
    public RedmiBudsClient Client { get; }
    public BudsState State { get; private set; } = new();
    public LinkStatus Status { get; private set; } = LinkStatus.Idle;
    public string StatusDetail { get; private set; } = "";

    public string Color
    {
        get => AppSettings.GetColor(Device.Mac) is { } c && Model.Colors.Contains(c) ? c : Model.Colors.FirstOrDefault() ?? "";
        set { AppSettings.SetColor(Device.Mac, value); Updated?.Invoke(); }
    }

    /// <summary>Custom name if the user set one, else the Bluetooth name.</summary>
    public string DisplayName
    {
        get => AppSettings.GetName(Device.Mac) ?? Device.Name;
        set { AppSettings.SetName(Device.Mac, value == Device.Name ? null : value); Updated?.Invoke(); }
    }

    public bool HasCustomName => AppSettings.GetName(Device.Mac) != null;

    /// <summary>State or status changed.</summary>
    public event Action? Updated;
    /// <summary>Raised once per connection when battery + firmware are known (drives the island popup).</summary>
    public event Action? Announce;

    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _kick = new(0);
    BluetoothDevice? _bt;
    bool _forced = true;
    bool _announced;

    public BudsController(PairedDevice device, BudsModel model)
    {
        Device = device;
        Model = model;
        Client = new RedmiBudsClient(device.Address);
        Client.Changed += s =>
        {
            State = s;
            if (s.Connected && !_announced && s.Firmware.Length > 0 && (s.Left.Known || s.Right.Known))
            {
                _announced = true;
                Announce?.Invoke();
            }
            Updated?.Invoke();
        };
    }

    public void Start() => _ = RunAsync(_cts.Token);

    public void Stop()
    {
        _cts.Cancel();
        Client.Disconnect();
        _bt?.Dispose();
    }

    public void Reconnect()
    {
        _forced = true;
        Client.Disconnect();
        Kick();
    }

    void Kick()
    {
        if (_kick.CurrentCount == 0) _kick.Release();
    }

    void SetStatus(LinkStatus s, string detail = "")
    {
        Status = s;
        StatusDetail = detail;
        Updated?.Invoke();
    }

    async Task RunAsync(CancellationToken ct)
    {
        try
        {
            _bt = await BluetoothDevice.FromBluetoothAddressAsync(Device.Address);
            if (_bt != null)
                _bt.ConnectionStatusChanged += (d, _) =>
                {
                    Log.Info($"{Device.Name}: link {d.ConnectionStatus}");
                    if (d.ConnectionStatus == BluetoothConnectionStatus.Connected) Kick();
                };
        }
        catch (Exception e) { Log.Warn("BluetoothDevice lookup failed: " + e.Message); }

        int backoff = 2;
        while (!ct.IsCancellationRequested)
        {
            bool linkUp = _bt?.ConnectionStatus == BluetoothConnectionStatus.Connected;
            if (linkUp || _forced)
            {
                _forced = false;
                SetStatus(LinkStatus.Connecting);
                var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnGone() => gone.TrySetResult();
                Client.Disconnected += OnGone;
                try
                {
                    Log.Info($"{Device.Name}: connecting…");
                    _announced = false;
                    await Client.ConnectAsync(ct);
                    Log.Info($"{Device.Name}: connected, auth={Client.State.Authenticated} fw={Client.State.Firmware}");
                    SetStatus(LinkStatus.Connected);
                    backoff = 2;
                    await gone.Task.WaitAsync(ct);
                    Log.Info($"{Device.Name}: disconnected");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    Log.Warn($"{Device.Name}: connect failed: {e.Message} (0x{e.HResult:X8})");
                    if ((uint)e.HResult == 0x80072740)
                        SetStatus(LinkStatus.Busy, "In use by another app");
                    else
                        SetStatus(LinkStatus.Error, e is RfcommException ? e.Message : "Couldn't connect");
                }
                finally
                {
                    Client.Disconnected -= OnGone;
                    Client.Disconnect();
                }
            }
            if (Status is LinkStatus.Connecting or LinkStatus.Connected) SetStatus(LinkStatus.Idle);

            linkUp = _bt?.ConnectionStatus == BluetoothConnectionStatus.Connected;
            var wait = linkUp ? TimeSpan.FromSeconds(backoff) : TimeSpan.FromSeconds(60);
            if (linkUp) backoff = Math.Min(backoff * 2, 30);
            try
            {
                if (!await _kick.WaitAsync(wait, ct) && !linkUp) _forced = true; // occasional probe
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Run a command; failures are logged, never thrown at the UI.</summary>
    public async void Run(Func<RedmiBudsClient, Task> action)
    {
        try { await action(Client); }
        catch (Exception e) { Log.Warn("command failed: " + e.Message); }
    }

    public string BatterySummary()
    {
        var s = State;
        if (!s.Connected) return $"{DisplayName} · not connected";
        var parts = new List<string>();
        if (s.Left.Known) parts.Add($"L {s.Left}");
        if (s.Right.Known) parts.Add($"R {s.Right}");
        if (s.Case.Known) parts.Add($"Case {s.Case}");
        return parts.Count == 0 ? DisplayName : $"{DisplayName} · {string.Join("  ", parts)}";
    }
}
