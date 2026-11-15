using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace XiaomiBuds.Controls;

/// <summary>Small helpers shared by the code-built controls.</summary>
public static class Ui
{
    /// <summary>Resolve a theme brush for an element's current light/dark theme.</summary>
    public static Brush Brush(FrameworkElement fe, string key)
    {
        string theme = fe.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var md in Application.Current.Resources.MergedDictionaries)
            if (md.ThemeDictionaries.TryGetValue(theme, out var td) && td is ResourceDictionary rd && rd.TryGetValue(key, out var v))
                return (Brush)v;
        return (Brush)Application.Current.Resources[key];
    }

    public static void Animate(DependencyObject target, string property, double to, double ms,
                               EasingFunctionBase? ease = null, bool dependent = false)
    {
        var a = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = ease ?? new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 },
            EnableDependentAnimation = dependent,
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, property);
        var sb = new Storyboard();
        sb.Children.Add(a);
        sb.Begin();
    }

    public static Uri Asset(string relative) => new(Path.Combine(AppContext.BaseDirectory, "Assets", relative));

    public static BitmapImage Image(string relative) => new(Asset(relative));

    /// <summary>
    /// Show a product photo from <see cref="Core.ImageStore"/> (local copy, cache, or downloaded from Xiaomi).
    /// Falls back to the app's own generic earbuds drawing while offline.
    /// </summary>
    public static async void SetDeviceImage(Microsoft.UI.Xaml.Controls.Image img, string key, string? url,
                                            string? fallback = "generic_buds.png", Action? onMissing = null,
                                            Core.ImageProcess process = Core.ImageProcess.Trim)
    {
        img.Tag = key;
        string? path = null;
        try { path = await Core.ImageStore.GetAsync(key, url, process); } catch { }
        if (img.Tag as string != key) return; // a newer request replaced this one
        if (path != null) img.Source = new BitmapImage(new Uri(path));
        else if (fallback != null) img.Source = Image(fallback);
        else onMissing?.Invoke();
    }
}
