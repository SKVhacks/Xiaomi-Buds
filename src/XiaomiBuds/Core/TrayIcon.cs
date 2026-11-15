using System.Runtime.InteropServices;

namespace XiaomiBuds.Core;

public sealed record TrayMenuItem(string Text, Action? OnClick = null, bool Checked = false, bool Enabled = true)
{
    public static readonly TrayMenuItem Separator = new("-");
}

/// <summary>Notification-area icon with a native context menu (Shell_NotifyIcon, no dependencies).</summary>
public sealed class TrayIcon : IDisposable
{
    public event Action? LeftClick;
    /// <summary>Called right before the menu opens to build its items.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider;

    const uint WM_TRAY = 0x8000 + 1;
    const uint WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_NULL = 0;
    readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    readonly WndProc _proc; // keep the delegate alive
    readonly IntPtr _hwnd, _icon;
    string _tip;

    public TrayIcon(string iconPath, string tooltip)
    {
        _tip = tooltip;
        _proc = Proc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "XiaomiBudsTray",
        };
        RegisterClassEx(ref wc);
        _hwnd = CreateWindowEx(0, wc.lpszClassName, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        int cx = GetSystemMetrics(49), cy = GetSystemMetrics(50); // SM_CXSMICON
        _icon = LoadImage(IntPtr.Zero, iconPath, 1, cx, cy, 0x10);
        try { SetPreferredAppMode(1); FlushMenuThemes(); } catch { } // dark context menu when Windows is dark
        Add();
    }

    void Add()
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_ADD, ref d);
    }

    public string Tooltip
    {
        set
        {
            _tip = value.Length > 127 ? value[..127] : value;
            var d = Data(NIF_TIP | NIF_SHOWTIP);
            Shell_NotifyIcon(NIM_MODIFY, ref d);
        }
    }

    /// <summary>Windows notification from the tray icon (shown as a toast on Windows 10/11).</summary>
    public void ShowBalloon(string title, string text)
    {
        var d = Data(NIF_INFO);
        d.szInfoTitle = title.Length > 63 ? title[..63] : title;
        d.szInfo = text.Length > 255 ? text[..255] : text;
        d.dwInfoFlags = 0x4 | 0x20; // NIIF_USER (our icon) | NIIF_LARGE_ICON
        d.hBalloonIcon = _icon;
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd, uID = 1, uFlags = flags, uCallbackMessage = WM_TRAY, hIcon = _icon, szTip = _tip,
        szInfo = "", szInfoTitle = "",
    };

    IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WM_TRAY)
        {
            uint ev = (uint)(l.ToInt64() & 0xFFFF);
            if (ev == WM_LBUTTONUP) LeftClick?.Invoke();
            else if (ev == WM_RBUTTONUP) ShowMenu();
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated) Add(); // Explorer restarted
        return DefWindowProc(h, msg, w, l);
    }

    void ShowMenu()
    {
        var items = MenuProvider?.Invoke() ?? [];
        var menu = CreatePopupMenu();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Text == "-") { AppendMenu(menu, MF_SEPARATOR, 0, null); continue; }
            uint f = MF_STRING | (it.Checked ? MF_CHECKED : 0) | (it.Enabled && it.OnClick != null ? 0 : MF_GRAYED);
            AppendMenu(menu, f, (uint)(i + 1), it.Text);
        }
        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        if (cmd > 0 && cmd <= items.Count) items[cmd - 1].OnClick?.Invoke();
    }

    public void Dispose()
    {
        var d = Data(0);
        Shell_NotifyIcon(NIM_DELETE, ref d);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        DestroyWindow(_hwnd);
    }

    // ------------------------------------------------------------------ Win32
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    const uint MF_STRING = 0, MF_GRAYED = 1, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
    const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20;

    delegate IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA d);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr inst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr m, uint flags, uint id, string? text);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr m);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr m, uint flags, int x, int y, IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();
}
