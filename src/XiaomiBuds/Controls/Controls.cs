using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace XiaomiBuds.Controls;

/// <summary>iOS-style toggle switch.</summary>
public sealed class IosSwitch : Grid
{
    readonly Border _track = new() { CornerRadius = new CornerRadius(15.5) };
    readonly TranslateTransform _tx = new();
    readonly Border _knob;
    bool _on;
    public event Action<bool>? Toggled;

    public IosSwitch()
    {
        Width = 51; Height = 31;
        Background = new SolidColorBrush(Colors.Transparent);
        VerticalAlignment = VerticalAlignment.Center;
        _knob = new Border
        {
            Width = 27, Height = 27, CornerRadius = new CornerRadius(13.5),
            Background = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x18, 0, 0, 0)), BorderThickness = new Thickness(0.5),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0),
            RenderTransform = _tx,
        };
        Children.Add(_track);
        Children.Add(_knob);
        Tapped += (_, _) =>
        {
            _on = !_on;
            Apply(true);
            Toggled?.Invoke(_on);
        };
        ActualThemeChanged += (_, _) => Apply(false);
        Loaded += (_, _) => Apply(false);
    }

    public bool IsOn
    {
        get => _on;
        set { if (_on != value) { _on = value; Apply(true); } }
    }

    void Apply(bool animate)
    {
        _track.Background = Ui.Brush(this, _on ? "GreenBrush" : "SwitchOffBrush");
        double x = _on ? 20 : 0;
        if (animate) Ui.Animate(_tx, "X", x, 320);
        else _tx.X = x;
    }
}

/// <summary>iOS-style segmented control with a sliding selection pill.</summary>
public sealed class Segmented : Grid
{
    readonly Grid _items = new();
    readonly Border _pill;
    readonly TranslateTransform _tx = new();
    readonly List<TextBlock> _labels = new();
    int _sel = -1;
    public event Action<int>? SelectionChanged;

    public Segmented(IEnumerable<string> items)
    {
        Height = 34;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(2);
        _pill = new Border
        {
            CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0.5), RenderTransform = _tx, Opacity = 0,
        };
        Children.Add(_pill);
        Children.Add(_items);
        int i = 0;
        foreach (var s in items)
        {
            _items.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tb = new TextBlock
            {
                Text = s, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var cell = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            cell.Children.Add(tb);
            SetColumn(cell, i);
            int idx = i;
            cell.Tapped += (_, _) =>
            {
                if (idx == _sel) return;
                Select(idx, true);
                SelectionChanged?.Invoke(idx);
            };
            _items.Children.Add(cell);
            _labels.Add(tb);
            i++;
        }
        SizeChanged += (_, _) => Layout(false);
        ActualThemeChanged += (_, _) => Colorize();
        Loaded += (_, _) => Colorize();
    }

    public int Selected
    {
        get => _sel;
        set { if (value != _sel) Select(value, IsLoaded); }
    }

    void Select(int i, bool animate)
    {
        _sel = i;
        Layout(animate);
        Colorize();
    }

    void Layout(bool animate)
    {
        if (_labels.Count == 0 || ActualWidth <= 0) return;
        double w = (ActualWidth - 4) / _labels.Count;
        _pill.Width = w;
        _pill.Opacity = _sel >= 0 ? 1 : 0;
        double x = Math.Max(0, _sel) * w;
        if (animate) Ui.Animate(_tx, "X", x, 380);
        else _tx.X = x;
    }

    void Colorize()
    {
        Background = Ui.Brush(this, "FillBrush");
        _pill.Background = Ui.Brush(this, "PillBrush");
        _pill.BorderBrush = Ui.Brush(this, "PillStrokeBrush");
        for (int i = 0; i < _labels.Count; i++)
        {
            _labels[i].Foreground = Ui.Brush(this, "Text1Brush");
            _labels[i].FontWeight = i == _sel ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}

/// <summary>"85%" next to a small iOS battery glyph; green while charging, red when low.</summary>
public sealed class BatteryLabel : StackPanel
{
    readonly TextBlock _text = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _outline, _fill;
    readonly Border _nub;
    readonly FontIcon _bolt = new() { Glyph = "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    int? _level;
    bool _charging;
    readonly bool _light;

    /// <param name="onDark">Always use white text (for the black island).</param>
    public BatteryLabel(bool onDark = false)
    {
        _light = onDark;
        Orientation = Orientation.Horizontal;
        Spacing = 5;
        HorizontalAlignment = HorizontalAlignment.Center;
        _fill = new Border { CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left };
        _outline = new Border
        {
            Width = 24, Height = 12, CornerRadius = new CornerRadius(3.5), BorderThickness = new Thickness(1),
            Padding = new Thickness(1.5), Child = _fill, VerticalAlignment = VerticalAlignment.Center,
        };
        _nub = new Border { Width = 1.5, Height = 4, CornerRadius = new CornerRadius(0, 1, 1, 0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-4, 0, 0, 0) };
        Children.Add(_bolt);
        Children.Add(_text);
        Children.Add(_outline);
        Children.Add(_nub);
        ActualThemeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    public void Set(int? level, bool charging)
    {
        _level = level;
        _charging = charging;
        Render();
    }

    void Render()
    {
        Brush text = _light ? new SolidColorBrush(Colors.White) : Ui.Brush(this, "Text1Brush");
        Brush muted = _light ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 255, 255, 255)) : Ui.Brush(this, "Text3Brush");
        _text.Text = _level is int l ? $"{l}%" : "—";
        _text.Foreground = _level is null ? muted : text;
        _bolt.Visibility = _charging && _level is not null ? Visibility.Visible : Visibility.Collapsed;
        _bolt.Foreground = Ui.Brush(this, "GreenBrush");
        _outline.BorderBrush = muted;
        _nub.Background = muted;
        _outline.Visibility = _nub.Visibility = _level is null ? Visibility.Collapsed : Visibility.Visible;
        double frac = (_level ?? 0) / 100.0;
        _fill.Width = Math.Max(1.5, 19 * frac);
        _fill.Background = _charging ? Ui.Brush(this, "GreenBrush")
                         : (_level ?? 100) <= 20 ? Ui.Brush(this, "RedBrush")
                         : text;
    }
}

/// <summary>Circular progress ring drawn with an arc path.</summary>
public sealed class Ring : Grid
{
    readonly Path _arc;
    readonly double _size, _thickness;

    public Ring(double size, double thickness, Brush track)
    {
        _size = size; _thickness = thickness;
        Width = Height = size;
        Children.Add(new Ellipse { Stroke = track, StrokeThickness = thickness, Margin = new Thickness(thickness / 2) });
        _arc = new Path { StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        Children.Add(_arc);
    }

    public Brush Stroke { set => _arc.Stroke = value; }

    public void SetValue01(double v)
    {
        v = Math.Clamp(v, 0, 1);
        if (v < 0.005) { _arc.Data = null; return; }
        double r = (_size - _thickness) / 2, c = _size / 2;
        double a = Math.Min(v, 0.9999) * 2 * Math.PI;
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        fig.Segments.Add(new ArcSegment
        {
            Point = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)),
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = a > Math.PI,
        });
        var g = new PathGeometry();
        g.Figures.Add(fig);
        _arc.Data = g;
    }
}
