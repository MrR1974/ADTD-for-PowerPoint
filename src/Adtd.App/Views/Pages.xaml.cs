using System.Windows;
using System.Windows.Controls;
using Adtd.App.ViewModels;

namespace Adtd.App.Views;

public partial class ConnectPage : Page
{
    readonly ConnectViewModel _vm;

    public ConnectPage(ConnectViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Loaded += async (_, _) => { if (vm.LocatedText == null) await vm.Locate(); };
        // The password lives in the PasswordBox and the view model only for one collection; clear the box after it.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectViewModel.IsBusy) && !vm.IsBusy) Password.Clear();
        };
    }

    void OnPasswordChanged(object sender, RoutedEventArgs e) => _vm.Password = Password.Password;
}

public partial class StylePage : Page
{
    public StylePage(StyleViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}

public partial class DiagramsPage : Page
{
    public DiagramsPage(DiagramsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}

public partial class PreviewPage : Page
{
    public PreviewPage(PreviewViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}

public partial class ExportPage : Page
{
    public ExportPage(ExportViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}

public partial class AboutPage : Page
{
    public AboutPage(AboutViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
