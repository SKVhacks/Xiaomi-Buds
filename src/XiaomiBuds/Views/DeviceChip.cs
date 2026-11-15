using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using XiaomiBuds.Controls;
using XiaomiBuds.Devices.RedmiBuds;

namespace XiaomiBuds.Views;

/// <summary>One pair of earbuds in the device bar: photo, name, live status. Tap to switch to it.</summary>
public sealed class DeviceChip : Grid
{
    public BudsController Controller { get; }
    public event Action<DeviceChip>? Picked;

    readonly Border _card;
    readonly Image _img = new() { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _name = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _status = new() { FontSize = 11.5 };
    readonly Ellipse _dot = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
    bool _selected, _hover;

    public DeviceChip(BudsController controller)
    {
        Controller = controller;
        ModelArt.Hero(_img, controller.Model);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(_name);
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        statusRow.Children.Add(_dot);
        statusRow.Children.Add(_status);
        text.Children.Add(statusRow);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(_img);
        row.Children.Add(text);

        _card = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 6, 14, 6),
            BorderThickness = new Thickness(1.5), Child = row,
        };
        Children.Add(_card);

        Tapped += (_, _) => Picked?.Invoke(this);
        PointerEntered += (_, _) => { _hover = true; Paint(); };
        PointerExited += (_, _) => { _hover = false; Paint(); };
        ActualThemeChanged += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Paint(); }
    }

    /// <summary>Re-read name and status from the controller.</summary>
    public void Refresh()
    {
        var c = Controller;
        var s = c.State;
        _name.Text = c.DisplayName;
        string dot;
        if (c.Status == LinkStatus.Connected && s.Connected)
        {
            int?[] levels = [s.Left.Level, s.Right.Level];
            var known = levels.Where(l => l.HasValue).Select(l => l!.Value).ToList();
            bool charging = s.Left.Charging || s.Right.Charging;
            _status.Text = known.Count > 0 ? $"{known.Min()}%{(charging ? " · charging" : "")}" : "Connected";
            dot = "GreenBrush";
        }
        else if (c.Status == LinkStatus.Connecting)
        {
            _status.Text = "Connecting…";
            dot = "OrangeBrush";
        }
        else
        {
            _status.Text = "Not connected";
            dot = "Text3Brush";
        }
        _dot.Fill = Ui.Brush(this, dot);
        ToolTipService.SetToolTip(this, c.BatterySummary());
        Paint();
    }

    void Paint()
    {
        _card.Background = Ui.Brush(this, _selected ? "CardBrush" : _hover ? "FillHoverBrush" : "FillBrush");
        _card.BorderBrush = _selected ? Ui.Brush(this, "AccentBrushX") : new SolidColorBrush(Colors.Transparent);
        _name.Foreground = Ui.Brush(this, "Text1Brush");
        _status.Foreground = Ui.Brush(this, "Text2Brush");
    }
}
