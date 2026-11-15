using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinUIEx;
using XiaomiBuds.Controls;
using XiaomiBuds.Devices.RedmiBuds;

namespace XiaomiBuds.Views;

/// <summary>
/// "Dynamic Island" popup that drops from the top-center of the screen when the buds connect:
/// the pill springs open, the case scales in, both buds fly out of it and the battery rings fill.
/// Everything is driven per frame from one clock so the motion stays coherent.
/// </summary>
public sealed partial class IslandWindow : Window
{
    public event Action? Clicked;

    const double WinW = 480, WinH = 214;
    const double SmallW = 126, SmallH = 36, BigW = 440, BigH = 184;
    const double CollapseAt = 5.2;
    const double BudTravel = 132;

    readonly IntPtr _hwnd;
    readonly CompositeTransform _caseT = new(), _leftT = new(), _rightT = new(), _heroT = new();
    readonly Slot[] _slots;
    bool _running, _hover, _hero;
    DateTime _last;
    double _t;

    sealed class Slot
    {
        public required Grid Host;
        public required Ring Ring;
        public required TextBlock Text;
        public required FontIcon Bolt;
        public required TextBlock Label;
        public double Target;
        public bool Known;
    }

    public IslandWindow()
    {
        InitializeComponent();
        SystemBackdrop = new TransparentTintBackdrop();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var p = OverlappedPresenter.Create();
        p.IsAlwaysOnTop = true;
        p.IsResizable = false;
        p.IsMaximizable = false;
        p.IsMinimizable = false;
        p.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(p);
        AppWindow.IsShownInSwitchers = false;

        // no non-client frame at all: the client area is the whole window
        _subclass = (h, msg, w, l, id, data) =>
            msg == 0x83 /*WM_NCCALCSIZE*/ && w != IntPtr.Zero ? IntPtr.Zero : DefSubclassProc(h, msg, w, l);
        SetWindowSubclass(_hwnd, _subclass, 1, 0);
        StripFrame();
        int pref = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(_hwnd, 33, ref pref, sizeof(int));
        int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE
        DwmSetWindowAttribute(_hwnd, 34, ref noBorder, sizeof(int));

        CaseImg.RenderTransform = _caseT;
        LeftImg.RenderTransform = _leftT;
        RightImg.RenderTransform = _rightT;
        _slots = [MakeSlot(LeftBatt, "L"), MakeSlot(CaseBatt, "Case"), MakeSlot(RightBatt, "R")];
        HeroImg.RenderTransform = _heroT;

        Root.PointerEntered += (_, _) => _hover = true;
        Root.PointerExited += (_, _) => _hover = false;
        Pill.Tapped += (_, _) =>
        {
            Finish();
            Clicked?.Invoke();
        };
    }

    readonly SubclassProc _subclass;

    void StripFrame()
    {
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        ex &= ~(0x1L | 0x100L | 0x200L | 0x20000L); // DLGMODALFRAME, WINDOWEDGE, CLIENTEDGE, STATICEDGE
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        long style = GetWindowLongPtr(_hwnd, GWL_STYLE);
        style &= ~(0x00800000L | 0x00400000L | 0x00040000L | 0x00080000L); // BORDER, DLGFRAME, THICKFRAME, SYSMENU
        SetWindowLongPtr(_hwnd, GWL_STYLE, style | unchecked((long)0x80000000L)); // WS_POPUP
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | 0x20 /*FRAMECHANGED*/ | 0x4 /*NOZORDER*/);
    }

    static Slot MakeSlot(Grid host, string label)
    {
        var caption = new TextBlock
        {
            Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 255, 255, 255)),
        };
        var ring = new Ring(18, 3, new SolidColorBrush(Windows.UI.Color.FromArgb(0x38, 255, 255, 255)));
        var text = new TextBlock { Foreground = new SolidColorBrush(Colors.White), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var bolt = new FontIcon { Glyph = "", FontSize = 11, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x30, 0xD1, 0x58)), VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(caption);
        row.Children.Add(ring);
        row.Children.Add(text);
        row.Children.Add(bolt);
        host.Children.Add(row);
        return new Slot { Host = host, Ring = ring, Text = text, Bolt = bolt, Label = caption };
    }

    public void Play(string name, BudsModel model, string color, BudsState s)
    {
        NameText.Text = name;
        _hero = !model.HasBudArt;
        if (_hero)
        {
            ModelArt.Hero(HeroImg, model);
        }
        else
        {
            ModelArt.Case(CaseImg, model, color);
            ModelArt.Left(LeftImg, model, color);
            ModelArt.Right(RightImg, model, color);
        }
        HeroImg.Visibility = _hero ? Visibility.Visible : Visibility.Collapsed;
        CaseImg.Visibility = LeftImg.Visibility = RightImg.Visibility = _hero ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 0; i < 3; i++) _slots[i].Label.Visibility = _hero ? Visibility.Visible : Visibility.Collapsed;
        Battery[] bats = [s.Left, s.Case, s.Right];
        for (int i = 0; i < 3; i++)
        {
            var b = bats[i];
            var sl = _slots[i];
            sl.Known = b.Known;
            sl.Target = (b.Level ?? 0) / 100.0;
            sl.Bolt.Visibility = b.Charging && b.Known ? Visibility.Visible : Visibility.Collapsed;
            sl.Ring.Stroke = new SolidColorBrush(b.Charging ? Windows.UI.Color.FromArgb(255, 0x30, 0xD1, 0x58)
                : (b.Level ?? 100) <= 20 ? Windows.UI.Color.FromArgb(255, 0xFF, 0x45, 0x3A)
                : Colors.White);
        }

        PlaceOnScreen();
        _t = 0;
        _last = DateTime.Now;
        Frame(0);
        AppWindow.Show(false);
        StripFrame();
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        if (!_running)
        {
            _running = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    void PlaceOnScreen()
    {
        var area = DisplayArea.Primary;
        var b = area.OuterBounds;
        // move onto the target monitor first so we read that monitor's DPI
        AppWindow.Move(new PointInt32(b.X + b.Width / 2, b.Y));
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int w = (int)Math.Round(WinW * scale), h = (int)Math.Round(WinH * scale);
        AppWindow.MoveAndResize(new RectInt32(b.X + (b.Width - w) / 2, b.Y, w, h));
    }

    void OnRendering(object? sender, object e)
    {
        var now = DateTime.Now;
        double dt = (now - _last).TotalSeconds;
        _last = now;
        // hovering keeps the island open
        if (!(_hover && _t > 1.8 && _t < CollapseAt - 0.3)) _t += dt;
        Frame(_t);
        if (_t > CollapseAt + 0.75) Finish();
    }

    void Frame(double t)
    {
        double open = Spring(t - 0.05, 0.62, 0.70);
        double close = Spring(t - CollapseAt, 0.42, 0.95);
        double k = open * (1 - close);
        Pill.Width = Lerp(SmallW, BigW, k);
        Pill.Height = Math.Max(SmallH * 0.6, Lerp(SmallH, BigH, k));
        Pill.CornerRadius = new CornerRadius(Lerp(SmallH / 2, 44, Math.Clamp(k, 0, 1)));
        Pill.Opacity = Clamp01(t / 0.12) * (1 - Clamp01((t - CollapseAt - 0.45) / 0.18));

        Stage.Opacity = Clamp01((t - 0.26) / 0.22) * (1 - Clamp01((t - CollapseAt + 0.2) / 0.2));

        double cs = Spring(t - 0.3, 0.55, 0.6);
        _caseT.ScaleX = _caseT.ScaleY = 0.55 + 0.45 * cs;
        _caseT.TranslateY = 14 * (1 - cs);
        CaseImg.Opacity = Clamp01((t - 0.3) / 0.18);

        // single product photo: rises and springs into place with a slight turn
        _heroT.ScaleX = _heroT.ScaleY = 0.5 + 0.5 * cs;
        _heroT.TranslateY = 26 * (1 - cs);
        _heroT.Rotation = -8 * (1 - cs);
        HeroImg.Opacity = Clamp01((t - 0.3) / 0.2);

        // buds start inside the case and fly out to their sides
        double bl = Spring(t - 0.68, 0.62, 0.62), br = Spring(t - 0.76, 0.62, 0.62);
        SetBud(_leftT, LeftImg, bl, +BudTravel, t - 0.68, -14);
        SetBud(_rightT, RightImg, br, -BudTravel, t - 0.76, 14);

        double fill = EaseOut((t - 1.0) / 1.1);
        for (int i = 0; i < 3; i++)
        {
            var sl = _slots[i];
            sl.Host.Opacity = Clamp01((t - 0.95) / 0.25);
            sl.Ring.SetValue01(sl.Known ? sl.Target * fill : 0);
            sl.Text.Text = sl.Known ? $"{Math.Round(sl.Target * 100 * fill)}%" : "—";
        }
    }

    static void SetBud(CompositeTransform tr, Image img, double p, double fromX, double since, double tilt)
    {
        tr.TranslateX = fromX * (1 - p);
        tr.TranslateY = 6 * (1 - p);
        tr.ScaleX = tr.ScaleY = 0.3 + 0.7 * p;
        tr.Rotation = tilt * (1 - p);
        img.Opacity = Clamp01(since / 0.12);
    }

    public void Finish()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
        AppWindow.Hide();
    }

    // damped spring step response (0 -> 1 with slight overshoot)
    static double Spring(double t, double period, double zeta)
    {
        if (t <= 0) return 0;
        double w = 2 * Math.PI / period;
        double wd = w * Math.Sqrt(1 - zeta * zeta);
        return 1 - Math.Exp(-zeta * w * t) * (Math.Cos(wd * t) + zeta * w / wd * Math.Sin(wd * t));
    }

    static double Clamp01(double x) => Math.Clamp(x, 0, 1);
    static double EaseOut(double x) { x = Clamp01(x); return 1 - Math.Pow(1 - x, 3); }
    static double Lerp(double a, double b, double k) => a + (b - a) * k;

    // ------------------------------------------------------------------ Win32
    const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern long GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern long SetWindowLongPtr(IntPtr h, int i, long v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    delegate IntPtr SubclassProc(IntPtr h, uint msg, IntPtr w, IntPtr l, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, SubclassProc p, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
