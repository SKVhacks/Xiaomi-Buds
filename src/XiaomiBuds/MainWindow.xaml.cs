using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using XiaomiBuds.Controls;
using XiaomiBuds.Core;
using XiaomiBuds.Devices.RedmiBuds;
using XiaomiBuds.Views;

namespace XiaomiBuds;

public sealed partial class MainWindow : Window
{
    readonly DeviceDiscovery _discovery = new();
    readonly Dictionary<ulong, (BudsController Ctrl, BudsView View)> _devices = new();
    readonly PairView _pair = new();
    readonly TrayIcon _tray;
    readonly IntPtr _hwnd;
    IslandWindow? _island;
    ulong? _selected;
    bool _quitting;
    void SetDeviceContent(UIElement content) => Host.Content = content;

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(iconPath);
        TitleIcon.Source = Ui.Image("AppIcon64.png");
        ApplyCaptionColors();
        RootGrid.ActualThemeChanged += (_, _) => ApplyCaptionColors();

        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int w = (int)(560 * scale), h = (int)(920 * scale);
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        h = Math.Min(h, work.Height - (int)(40 * scale));
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = (int)(420 * scale);
            op.PreferredMinimumHeight = (int)(520 * scale);
        }

        // closing hides to the tray; Quit lives in the tray menu
        AppWindow.Closing += (s, e) =>
        {
            if (_quitting) return;
            e.Cancel = true;
            s.Hide();
        };

        _tray = new TrayIcon(iconPath, "Xiaomi Buds");
        _tray.LeftClick += ShowAndActivate;
        _tray.MenuProvider = BuildTrayMenu;

        _discovery.Changed += list => DispatcherQueue.TryEnqueue(() => OnDevices(list));
        _discovery.Start();

        // --demo-island[=Model Name] previews the popup with sample data (no buds needed)
        var demo = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--demo-island"));
        if (demo != null)
        {
            var name = demo.Contains('=') ? demo[(demo.IndexOf('=') + 1)..] : "Redmi Buds 5";
            var model = BudsCatalog.Match(name) ?? BudsCatalog.Generic;
            var t = DispatcherQueue.CreateTimer();
            t.Interval = TimeSpan.FromSeconds(1.5);
            t.IsRepeating = false;
            t.Tick += (_, _) =>
            {
                _island ??= new IslandWindow();
                var s = new BudsState { Connected = true, Left = new(100, true), Right = new(85, false), Case = new(37, false) };
                string color = model.Colors.Length == 0 ? "" : model.Colors[0];
                _island.Play(name, model, color, s);
            };
            t.Start();
        }
    }

    public void ShowAndActivate()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
        SetForegroundWindow(_hwnd);
    }

    void ApplyCaptionColors()
    {
        var tb = AppWindow.TitleBar;
        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var fg = dark ? Colors.White : Colors.Black;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = fg;
        tb.ButtonHoverForegroundColor = fg;
        tb.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x18, 255, 255, 255) : Windows.UI.Color.FromArgb(0x10, 0, 0, 0);
        tb.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x28, 255, 255, 255) : Windows.UI.Color.FromArgb(0x20, 0, 0, 0);
        tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0x80, fg.R, fg.G, fg.B);
    }

    // ------------------------------------------------------------------ devices

    void OnDevices(IReadOnlyList<PairedDevice> paired)
    {
        // only devices this app can actually control are shown
        if (Environment.GetCommandLineArgs().Contains("--demo-devices"))
        {
            // simulated extra buds to preview the multi-device layout (they never connect)
            paired = [.. paired, new PairedDevice("Redmi Buds 6 Pro", 0x02AABBCCDD01), new PairedDevice("Xiaomi Buds 5", 0x02AABBCCDD02)];
        }
        var supported = paired.Select(d => (Dev: d, Model: BudsCatalog.Match(d.Name)))
                              .Where(x => x.Model != null).ToList();

        foreach (var addr in _devices.Keys.Except(supported.Select(x => x.Dev.Address)).ToList())
        {
            _devices[addr].Ctrl.Stop();
            _devices.Remove(addr);
        }
        foreach (var (dev, model) in supported)
        {
            if (_devices.ContainsKey(dev.Address)) continue;
            // fetch the product photo now so the first connect popup already has it
            _ = ImageStore.GetAsync($"{model!.Art}/hero.png", model.HeroUrl);
            if (model.BudUrl != null && !model.SplitArt)
            {
                _ = ImageStore.GetAsync($"{model.Art}/bud_right.png", model.BudUrl, ImageProcess.IsolateBud);
                _ = ImageStore.GetAsync($"{model.Art}/bud_left.png", model.BudUrl, ImageProcess.IsolateBudMirrored);
            }
            var ctrl = new BudsController(dev, model);
            var view = new BudsView(ctrl);
            ctrl.Updated += () => DispatcherQueue.TryEnqueue(() =>
            {
                // follow the buds you're using: if these just connected and the ones on screen aren't, switch
                if (ctrl.Status == LinkStatus.Connected && _selected != ctrl.Device.Address
                    && CurrentDevice is { Status: not LinkStatus.Connected })
                    Select(ctrl.Device.Address);
                UpdateTray();
                RefreshStrip();
            });
            ctrl.Announce += () => DispatcherQueue.TryEnqueue(() => ShowIsland(ctrl));
            _devices[dev.Address] = (ctrl, view);
            ctrl.Start();
        }

        if (_devices.Count == 0)
        {
            _selected = null;
            SetDeviceContent(_pair);
        }
        else
        {
            if (_selected is not ulong s || !_devices.ContainsKey(s))
            {
                var last = AllDevices.FirstOrDefault(c => c.Device.Mac == AppSettings.LastDevice);
                _selected = (last ?? AllDevices.First()).Device.Address;
            }
            Select(_selected.Value);
        }
        RebuildStrip();
        UpdateTray();
    }

    void Select(ulong address)
    {
        _selected = address;
        AppSettings.LastDevice = _devices[address].Ctrl.Device.Mac;
        SetDeviceContent(_devices[address].View);
        RefreshStrip();
        UpdateTray();
    }

    // ------------------------------------------------------------------ device bar (multiple earbuds)

    void RebuildStrip()
    {
        DeviceStrip.Children.Clear();
        foreach (var c in _devices.Values.Select(v => v.Ctrl).OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var chip = new DeviceChip(c);
            chip.Picked += ch => Select(ch.Controller.Device.Address);
            DeviceStrip.Children.Add(chip);
        }
        RefreshStrip();
    }

    void RefreshStrip()
    {
        foreach (var chip in DeviceStrip.Children.OfType<DeviceChip>())
        {
            chip.Selected = chip.Controller.Device.Address == _selected;
            chip.Refresh();
        }
        DeviceStripHost.Visibility = _devices.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    BudsController? CurrentDevice => _selected is ulong s && _devices.TryGetValue(s, out var d) ? d.Ctrl : null;

    void ShowIsland(BudsController c)
    {
        try
        {
            if (_island is null)
            {
                _island = new IslandWindow();
                _island.Clicked += () =>
                {
                    if (_island != null && _devices.ContainsKey(c.Device.Address)) Select(c.Device.Address);
                    ShowAndActivate();
                };
            }
            _island.Play(c.DisplayName, c.Model, c.Color, c.State);
        }
        catch (Exception e) { Log.Error("island failed", e); }
    }

    // ------------------------------------------------------------------ tray

    IEnumerable<BudsController> AllDevices =>
        _devices.Values.Select(v => v.Ctrl).OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);

    void UpdateTray()
    {
        var lines = AllDevices.Select(c => c.BatterySummary()).ToList();
        _tray.Tooltip = lines.Count == 0 ? "Xiaomi Buds · no devices paired" : string.Join("\n", lines);
    }

    IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var items = new List<TrayMenuItem>();
        bool multi = _devices.Count > 1;
        foreach (var c in AllDevices)
        {
            items.Add(new TrayMenuItem(c.BatterySummary(), Enabled: false));
            bool on = c.Status == LinkStatus.Connected;
            var f = c.Features;
            if (on && f.Modes.Length > 0)
            {
                var mode = c.State.Mode;
                string pad = multi ? "    " : "";
                foreach (var m in BudsCatalog.AllModes.Where(f.Modes.Contains))
                {
                    var target = m;
                    string label = m switch
                    {
                        SoundMode.NoiseCancelling => "Noise cancellation",
                        SoundMode.Transparency => "Transparency",
                        _ => "Off",
                    };
                    items.Add(new TrayMenuItem(pad + label, () => c.Run(cl => cl.SetModeAsync(target)), mode == m));
                }
            }
            if (on) items.Add(new TrayMenuItem((multi ? "    " : "") + "Show battery popup", () => ShowIsland(c)));
            else items.Add(new TrayMenuItem((multi ? "    " : "") + "Reconnect", c.Reconnect));
            items.Add(TrayMenuItem.Separator);
        }
        items.Add(new TrayMenuItem("Open Xiaomi Buds", ShowAndActivate));
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem("Quit", Quit));
        return items;
    }

    void Quit()
    {
        _quitting = true;
        foreach (var (ctrl, _) in _devices.Values) ctrl.Stop();
        _discovery.Dispose();
        _tray.Dispose();
        _island?.Close();
        Close();
        Application.Current.Exit();
    }

    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
}
