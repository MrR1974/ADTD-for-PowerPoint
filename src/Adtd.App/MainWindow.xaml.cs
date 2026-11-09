using Adtd.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Adtd.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        ApplicationThemeManager.ApplySystemTheme();
        Nav.SetServiceProvider(services);
        Loaded += (_, _) =>
        {
            SystemThemeWatcher.Watch(this);
            Nav.Navigate(typeof(ConnectPage));
        };
    }
}
