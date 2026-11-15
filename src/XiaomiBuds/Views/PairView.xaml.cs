using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XiaomiBuds.Controls;

namespace XiaomiBuds.Views;

public sealed partial class PairView : UserControl
{
    public PairView()
    {
        InitializeComponent();
        var model = Devices.RedmiBuds.BudsCatalog.Match("Redmi Buds 5")!;
        Views.ModelArt.Hero(Hero, model);
    }

    async void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));
}
