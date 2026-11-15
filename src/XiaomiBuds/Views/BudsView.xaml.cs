using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using XiaomiBuds.Controls;
using XiaomiBuds.Devices.RedmiBuds;

namespace XiaomiBuds.Views;

/// <summary>Paths to a model's product images.</summary>
public static class ModelArt
{
    public static void Hero(Image img, BudsModel m) => Ui.SetDeviceImage(img, $"{m.Art}/hero.png", m.HeroUrl);

    // With a local per-color set use it; otherwise the case is the official hero photo
    // and the buds are cut out of the official single-bud photo (mirrored for the left one).
    public static void Case(Image img, BudsModel m, string color)
    {
        if (m.SplitArt) Ui.SetDeviceImage(img, $"{m.Art}/case_{color.ToLowerInvariant()}.png", null);
        else Hero(img, m);
    }

    public static void Left(Image img, BudsModel m, string color)
    {
        if (m.SplitArt) Ui.SetDeviceImage(img, $"{m.Art}/bud_left_{color.ToLowerInvariant()}.png", null);
        else Ui.SetDeviceImage(img, $"{m.Art}/bud_left.png", m.BudUrl, process: Core.ImageProcess.IsolateBudMirrored);
    }

    public static void Right(Image img, BudsModel m, string color)
    {
        if (m.SplitArt) Ui.SetDeviceImage(img, $"{m.Art}/bud_right_{color.ToLowerInvariant()}.png", null);
        else Ui.SetDeviceImage(img, $"{m.Art}/bud_right.png", m.BudUrl, process: Core.ImageProcess.IsolateBud);
    }
}

/// <summary>The control page for one pair of earbuds. Sections appear only for features the model supports.</summary>
public sealed partial class BudsView : UserControl
{
    readonly BudsController _c;
    readonly BatteryLabel _leftBatt = new(), _caseBatt = new(), _rightBatt = new();

    // display order of the strength options
    static readonly AncStrength[] AncDisplay = [AncStrength.Light, AncStrength.Balanced, AncStrength.Deep, AncStrength.Adaptive];
    static readonly TransparencyStrength[] TrDisplay = [TransparencyStrength.Regular, TransparencyStrength.Voice, TransparencyStrength.Ambient];
    static readonly EqPreset[] EqDisplay = [EqPreset.Standard, EqPreset.Balanced, EqPreset.Voice, EqPreset.Bass, EqPreset.Treble, EqPreset.Volume];

    // rebuilt whenever the feature set changes
    readonly List<(SoundMode Mode, ModeButton Button)> _modes = new();
    AncStrength[] _ancLevels = [];
    TransparencyStrength[] _trLevels = [];
    Segmented? _ancSeg, _trSeg;
    readonly List<(EqPreset Preset, FontIcon Check)> _eqRows = new();
    readonly IosSwitch _adaptiveAncSw = new(), _adaptiveSoundSw = new(), _earSw = new(), _dualSw = new(), _answerSw = new();
    readonly TextBlock _fwText = Value(), _addrText = Value(), _hwText = Value(), _modelText = Value();
    readonly TextBlock _nameText = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
    string _featureKey = "\0";
    string _shownArt = "";

    public BudsController Controller => _c;

    public BudsView(BudsController controller)
    {
        _c = controller;
        InitializeComponent();

        LeftBattHost.Content = _leftBatt;
        CaseBattHost.Content = _caseBatt;
        RightBattHost.Content = _rightBatt;

        _adaptiveAncSw.Toggled += on => _c.Run(cl => cl.SetAdaptiveAncAsync(on));
        _adaptiveSoundSw.Toggled += on => _c.Run(cl => cl.SetAdaptiveSoundAsync(on));
        _earSw.Toggled += on => _c.Run(cl => cl.SetEarDetectionAsync(on));
        _dualSw.Toggled += on => _c.Run(cl => cl.SetDoubleConnectionAsync(on));
        _answerSw.Toggled += on => _c.Run(cl => cl.SetAutoAnswerAsync(on));

        BuildAbout();
        // Xiaomi's logo is loaded from Xiaomi's site (like the product photos), not shipped in the repo
        Ui.SetDeviceImage(MiLogo, "Brand/mi-logo.png", "https://i02.appmifile.com/i18n/share/new-logo.png",
            fallback: null, onMissing: () => { MiLogo.Visibility = Visibility.Collapsed; MiText.Visibility = Visibility.Visible; });

        _c.Updated += () => DispatcherQueue.TryEnqueue(Render);
        ActualThemeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    // ------------------------------------------------------------------ building

    static TextBlock Value() => new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true };

    Grid Row(string title, UIElement? right, string? subtitle = null, Action? tap = null)
    {
        var g = new Grid { MinHeight = 50, Padding = new Thickness(16, 10, 16, 10), Background = new SolidColorBrush(Colors.Transparent) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStyle"] });
        if (subtitle != null)
            left.Children.Add(new TextBlock { Text = subtitle, Style = (Style)Application.Current.Resources["CaptionStyle"] });
        g.Children.Add(left);
        if (right != null)
        {
            if (right is FrameworkElement fe && fe.Parent is Panel old) old.Children.Remove(fe);
            Grid.SetColumn((FrameworkElement)right, 1);
            g.Children.Add(right);
        }
        if (tap != null)
        {
            g.Tapped += (_, _) => tap();
            g.PointerEntered += (_, _) => g.Background = Ui.Brush(this, "FillBrush");
            g.PointerExited += (_, _) => g.Background = new SolidColorBrush(Colors.Transparent);
        }
        return g;
    }

    void AddRows(Panel host, IEnumerable<UIElement> rows)
    {
        host.Children.Clear();
        bool first = true;
        foreach (var r in rows)
        {
            if (!first)
            {
                var d = new Rectangle { Height = 1, Margin = new Thickness(16, 0, 0, 0) };
                d.Loaded += (_, _) => d.Fill = Ui.Brush(this, "SepBrush");
                d.ActualThemeChanged += (_, _) => d.Fill = Ui.Brush(this, "SepBrush");
                host.Children.Add(d);
            }
            host.Children.Add(r);
            first = false;
        }
    }

    static string EqName(EqPreset p) => p switch
    {
        EqPreset.Standard => "Standard",
        EqPreset.Balanced => "Balanced",
        EqPreset.Voice => "Enhanced vocals",
        EqPreset.Bass => "Enhanced bass",
        EqPreset.Treble => "Enhanced treble",
        EqPreset.Volume => "Volume boost",
        _ => p.ToString(),
    };

    static string AncName(AncStrength a) => a switch
    {
        AncStrength.Light => "Light", AncStrength.Balanced => "Balanced", AncStrength.Deep => "Deep", _ => "Adaptive",
    };

    static string TrName(TransparencyStrength t) => t switch
    {
        TransparencyStrength.Regular => "Regular", TransparencyStrength.Voice => "Voice focus", _ => "Ambient",
    };

    /// <summary>(Re)create the sections that depend on the model's feature set.</summary>
    void BuildFeatures(BudsModel f)
    {
        // noise control
        ModeRow.Children.Clear();
        ModeRow.ColumnDefinitions.Clear();
        _modes.Clear();
        foreach (var mode in BudsCatalog.AllModes.Where(f.Modes.Contains))
        {
            var b = mode switch
            {
                SoundMode.NoiseCancelling => new ModeButton("Noise cancellation", ModeIcon.Anc),
                SoundMode.Transparency => new ModeButton("Transparency", ModeIcon.Transparency),
                _ => new ModeButton("Off", ModeIcon.Off),
            };
            ModeRow.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(b, _modes.Count);
            ModeRow.Children.Add(b);
            var m = mode;
            b.Click += () => _c.Run(cl => cl.SetModeAsync(m));
            _modes.Add((mode, b));
        }
        _ancLevels = AncDisplay.Where(f.AncLevels.Contains).ToArray();
        _trLevels = TrDisplay.Where(f.TransparencyLevels.Contains).ToArray();
        _ancSeg = _ancLevels.Length > 1 ? new Segmented(_ancLevels.Select(AncName)) : null;
        _trSeg = _trLevels.Length > 1 ? new Segmented(_trLevels.Select(TrName)) : null;
        if (_ancSeg != null) _ancSeg.SelectionChanged += i => _c.Run(cl => cl.SetAncLevelAsync(_ancLevels[i]));
        if (_trSeg != null) _trSeg.SelectionChanged += i => _c.Run(cl => cl.SetTransparencyLevelAsync(_trLevels[i]));
        AddRows(AdaptiveAncHost, f.AdaptiveAnc
            ? [Row("Adaptive noise cancellation", _adaptiveAncSw, "Adjusts the level to your surroundings")]
            : []);
        NoiseSection.Visibility = _modes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // sound
        _eqRows.Clear();
        var soundRows = new List<UIElement>();
        foreach (var p in EqDisplay.Where(f.EqPresets.Contains))
        {
            var check = new FontIcon { Glyph = "", FontSize = 15, FontWeight = FontWeights.Bold, Opacity = 0 };
            _eqRows.Add((p, check));
            var preset = p;
            soundRows.Add(Row(EqName(p), check, tap: () => _c.Run(cl => cl.SetEqAsync(preset))));
        }
        if (f.AdaptiveSound)
            soundRows.Add(Row("Adaptive sound", _adaptiveSoundSw, "Tunes the sound to the fit of your earbuds"));
        AddRows(SoundList, soundRows);
        SoundSection.Visibility = soundRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // find
        FindSection.Visibility = f.Find ? Visibility.Visible : Visibility.Collapsed;
        FindLeft.Visibility = FindRight.Visibility = f.FindPerBud ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(FindBoth, f.FindPerBud ? 1 : 0);
        Grid.SetColumnSpan(FindBoth, f.FindPerBud ? 1 : 3);
        FindBoth.Content = f.FindPerBud ? "Both" : "Play sound";

        // settings
        var settings = new List<UIElement>();
        if (f.EarDetection) settings.Add(Row("In-ear detection", _earSw, "Pause playback when you take an earbud out"));
        if (f.DualConnection) settings.Add(Row("Dual-device connection", _dualSw, "Stay connected to this PC and your phone"));
        if (f.AutoAnswer) settings.Add(Row("Auto-answer calls", _answerSw, "Answer incoming calls when you put the buds in"));
        AddRows(SettingsList, settings);
        SettingsSection.Visibility = settings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void BuildAbout()
    {
        foreach (var color in _c.Model.Colors)
        {
            var sw = new Swatch(SwatchColor(color), color);
            sw.Tapped += (_, _) => _c.Color = color;
            _swatches.Children.Add(sw);
        }
        var nameRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        nameRight.Children.Add(_nameText);
        nameRight.Children.Add(new FontIcon { Glyph = "", FontSize = 11, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
        var rows = new List<UIElement>
        {
            Row("Name", nameRight, tap: () => _ = RenameAsync()),
            Row("Model", _modelText),
        };
        if (_c.Model.SplitArt) rows.Add(Row("Color", _swatches));
        rows.Add(Row("Firmware", _fwText));
        rows.Add(Row("Hardware", _hwText));
        rows.Add(Row("Bluetooth address", _addrText));
        AddRows(AboutList, rows);
    }

    static Windows.UI.Color SwatchColor(string c) => c switch
    {
        "Black" => Windows.UI.Color.FromArgb(255, 0x22, 0x22, 0x24),
        "Blue" => Windows.UI.Color.FromArgb(255, 0xC9, 0xD6, 0xF2),
        _ => Windows.UI.Color.FromArgb(255, 0xF5, 0xF5, 0xF5),
    };

    public void SetSwitcher(IReadOnlyList<BudsController> all, Action<BudsController> pick)
    {
        bool multi = all.Count > 1;
        NameChevron.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        NameButton.IsHitTestVisible = multi;
        if (!multi) { NameButton.Flyout = null; return; }
        var menu = new MenuFlyout();
        foreach (var c in all)
        {
            var item = new ToggleMenuFlyoutItem { Text = c.DisplayName, IsChecked = c == _c };
            item.Click += (_, _) => pick(c);
            menu.Items.Add(item);
        }
        NameButton.Flyout = menu;
    }

    // ------------------------------------------------------------------ rendering

    void Render()
    {
        var s = _c.State;
        var model = _c.Model;
        bool on = _c.Status == LinkStatus.Connected && s.Connected;
        var f = _c.Features;

        var key = f.FeatureKey();
        if (key != _featureKey)
        {
            _featureKey = key;
            BuildFeatures(f);
        }

        NameText.Text = _c.DisplayName;
        (string text, string dot) = _c.Status switch
        {
            LinkStatus.Connected when s.Connected => ("Connected", "GreenBrush"),
            LinkStatus.Connecting => ("Connecting…", "OrangeBrush"),
            LinkStatus.Busy => ("In use by another app. Close it, then reconnect.", "RedBrush"),
            _ => ("Not connected · Open the case or put the buds in", "Text3Brush"),
        };
        StatusText.Text = text;
        StatusDot.Fill = Ui.Brush(this, dot);
        ReconnectButton.Visibility = on || _c.Status == LinkStatus.Connecting ? Visibility.Collapsed : Visibility.Visible;

        ExperimentalNote.Visibility = model.Experimental ? Visibility.Visible : Visibility.Collapsed;
        ExperimentalText.Text = model == BudsCatalog.Generic
            ? "This model isn't in the app's list yet. Battery and any settings the buds report will appear here; some controls may not work."
            : $"Support for {model.Name} is experimental. The app shows the settings these buds report; some may not work yet.";

        RenderArt(model, on, s);

        _leftBatt.Set(on ? s.Left.Level : null, s.Left.Charging);
        _rightBatt.Set(on ? s.Right.Level : null, s.Right.Charging);
        _caseBatt.Set(on ? s.Case.Level : null, s.Case.Charging);

        ControlsPanel.Opacity = on ? 1 : 0.45;
        ControlsPanel.IsHitTestVisible = on;

        // noise control
        var mode = s.Mode;
        foreach (var (m, b) in _modes) b.SetSelected(mode == m, this);
        if (mode == SoundMode.NoiseCancelling && _ancSeg != null)
        {
            StrengthPanel.Visibility = Visibility.Visible;
            StrengthTitle.Text = "Noise cancellation level";
            StrengthHost.Content = _ancSeg;
            _ancSeg.Selected = s.AncLevel is { } a ? Array.IndexOf(_ancLevels, a) : -1;
        }
        else if (mode == SoundMode.Transparency && _trSeg != null)
        {
            StrengthPanel.Visibility = Visibility.Visible;
            StrengthTitle.Text = "Transparency mode";
            StrengthHost.Content = _trSeg;
            _trSeg.Selected = s.TransparencyLevel is { } t ? Array.IndexOf(_trLevels, t) : -1;
        }
        else StrengthPanel.Visibility = Visibility.Collapsed;
        AdaptiveAncHost.Visibility = mode == SoundMode.NoiseCancelling && f.AdaptiveAnc ? Visibility.Visible : Visibility.Collapsed;
        _adaptiveAncSw.IsOn = s.AdaptiveAnc ?? false;

        foreach (var (p, check) in _eqRows)
        {
            check.Opacity = s.EqPreset == (int)p ? 1 : 0;
            check.Foreground = Ui.Brush(this, "AccentBrushX");
        }
        _adaptiveSoundSw.IsOn = s.AdaptiveSound ?? false;

        _earSw.IsOn = s.EarDetection ?? false;
        _dualSw.IsOn = s.DoubleConnection ?? false;
        _answerSw.IsOn = s.AutoAnswer ?? false;

        foreach (var sw in _swatches.Children.OfType<Swatch>())
            sw.SetSelected(sw.Key.Equals(_c.Color, StringComparison.OrdinalIgnoreCase), this);
        _modelText.Text = model == BudsCatalog.Generic ? "Unknown (generic)" : model.Name;
        _fwText.Text = s.Firmware.Length > 0 ? s.Firmware : "—";
        _hwText.Text = s.Hardware.Length > 0 ? s.Hardware : "—";
        _addrText.Text = _c.Device.Mac;
        _nameText.Text = _c.DisplayName;
        foreach (var t in new[] { _nameText, _modelText, _fwText, _hwText, _addrText }) t.Foreground = Ui.Brush(this, "Text2Brush");

        if (!on) SetFinding(false);
    }

    void RenderArt(BudsModel model, bool on, BudsState s)
    {
        string artKey = model.SplitArt ? _c.Color : model.HasBudArt ? "buds" : "hero";
        if (artKey != _shownArt)
        {
            _shownArt = artKey;
            if (model.HasBudArt)
            {
                ModelArt.Left(LeftImg, model, _c.Color);
                ModelArt.Right(RightImg, model, _c.Color);
                ModelArt.Case(CaseImg, model, _c.Color);
            }
            else ModelArt.Hero(HeroImg, model);
            var split = model.HasBudArt ? Visibility.Visible : Visibility.Collapsed;
            LeftImg.Visibility = RightImg.Visibility = CaseImg.Visibility = split;
            HeroImg.Visibility = model.HasBudArt ? Visibility.Collapsed : Visibility.Visible;
            ArtRow.Height = new GridLength(model.HasBudArt ? 150 : 200);
        }
        LeftImg.Opacity = on && s.Left.Known ? 1 : 0.4;
        RightImg.Opacity = on && s.Right.Known ? 1 : 0.4;
        CaseImg.Opacity = on && s.Case.Known ? 1 : 0.4;
        HeroImg.Opacity = on ? 1 : 0.45;
    }

    // ------------------------------------------------------------------ handlers

    void Reconnect_Click(object sender, RoutedEventArgs e) => _c.Reconnect();

    bool _renaming;

    /// <summary>Ask for a new display name. Shown in this app only; the Bluetooth name is unchanged.</summary>
    async Task RenameAsync()
    {
        if (_renaming || XamlRoot is null) return;
        _renaming = true;
        try
        {
            var box = new TextBox
            {
                Text = _c.DisplayName, PlaceholderText = _c.Device.Name, MaxLength = 32,
                SelectionStart = 0, SelectionLength = _c.DisplayName.Length,
            };
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(box);
            panel.Children.Add(new TextBlock
            {
                Text = $"Shown in this app, the popup and the tray. Your buds still appear as “{_c.Device.Name}” in Windows and on your phone.",
                Style = (Style)Application.Current.Resources["CaptionStyle"],
            });
            var dlg = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Rename earbuds",
                Content = panel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = ActualTheme,
            };
            if (_c.HasCustomName) dlg.SecondaryButtonText = "Reset";
            box.TextChanged += (_, _) => dlg.IsPrimaryButtonEnabled = box.Text.Trim().Length > 0;
            box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);

            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary) _c.DisplayName = box.Text.Trim();
            else if (result == ContentDialogResult.Secondary) _c.DisplayName = _c.Device.Name;
        }
        finally { _renaming = false; }
    }

    void Find(FindTarget t)
    {
        _c.Run(cl => cl.FindAsync(true, t));
        SetFinding(true);
    }

    void SetFinding(bool on) => FindStop.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    void FindLeft_Click(object sender, RoutedEventArgs e) => Find(FindTarget.Left);
    void FindRight_Click(object sender, RoutedEventArgs e) => Find(FindTarget.Right);
    void FindBoth_Click(object sender, RoutedEventArgs e) => Find(FindTarget.Both);

    void FindStop_Click(object sender, RoutedEventArgs e)
    {
        _c.Run(cl => cl.FindAsync(false, FindTarget.Both));
        SetFinding(false);
    }
}

public enum ModeIcon { Anc, Off, Transparency }

/// <summary>Large round mode button with a caption, HyperOS style.</summary>
public sealed class ModeButton : StackPanel
{
    readonly Ellipse _bg = new() { Width = 60, Height = 60 };
    readonly List<Shape> _strokes = new(), _fills = new();
    readonly TextBlock _label;
    bool _selected;
    public event Action? Click;

    public ModeButton(string label, ModeIcon icon)
    {
        Spacing = 9;
        HorizontalAlignment = HorizontalAlignment.Center;
        Background = new SolidColorBrush(Colors.Transparent);
        var circle = new Grid { Width = 60, Height = 60, HorizontalAlignment = HorizontalAlignment.Center };
        circle.Children.Add(_bg);
        switch (icon)
        {
            case ModeIcon.Anc:
                AddStroke(circle, 24, 2.2);
                AddFill(circle, 12);
                break;
            case ModeIcon.Off:
                AddStroke(circle, 22, 2.2);
                break;
            case ModeIcon.Transparency:
                var e = AddStroke(circle, 24, 2.2);
                e.StrokeDashArray = [1.4, 1.6];
                AddFill(circle, 7);
                break;
        }
        Children.Add(circle);
        _label = new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Children.Add(_label);
        Tapped += (_, _) => Click?.Invoke();
        PointerEntered += (_, _) => { if (!_selected) _bg.Opacity = 0.75; };
        PointerExited += (_, _) => _bg.Opacity = 1;
    }

    Ellipse AddStroke(Grid g, double size, double th)
    {
        var e = new Ellipse { Width = size, Height = size, StrokeThickness = th };
        _strokes.Add(e);
        g.Children.Add(e);
        return e;
    }

    void AddFill(Grid g, double size)
    {
        var e = new Ellipse { Width = size, Height = size };
        _fills.Add(e);
        g.Children.Add(e);
    }

    public void SetSelected(bool selected, FrameworkElement themeSource)
    {
        _selected = selected;
        _bg.Fill = Ui.Brush(themeSource, selected ? "AccentBrushX" : "FillBrush");
        Brush fg = selected ? new SolidColorBrush(Colors.White) : Ui.Brush(themeSource, "Text1Brush");
        foreach (var s in _strokes) s.Stroke = fg;
        foreach (var s in _fills) s.Fill = fg;
        _label.Foreground = Ui.Brush(themeSource, selected ? "AccentBrushX" : "Text1Brush");
        _label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }
}

/// <summary>Color swatch circle with a selection ring.</summary>
public sealed class Swatch : Grid
{
    readonly Ellipse _ring = new() { Width = 30, Height = 30, StrokeThickness = 2 };
    public string Key { get; }

    public Swatch(Windows.UI.Color color, string key)
    {
        Key = key;
        Width = Height = 30;
        Background = new SolidColorBrush(Colors.Transparent);
        ToolTipService.SetToolTip(this, key);
        Children.Add(_ring);
        Children.Add(new Ellipse
        {
            Width = 22, Height = 22, Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0x80, 0x80, 0x80)), StrokeThickness = 1,
        });
    }

    public void SetSelected(bool on, FrameworkElement themeSource) =>
        _ring.Stroke = on ? Ui.Brush(themeSource, "AccentBrushX") : new SolidColorBrush(Colors.Transparent);
}
