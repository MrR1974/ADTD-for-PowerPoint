using System.IO;
using System.Windows;
using Adtd.App.Services;
using Adtd.App.ViewModels;
using Adtd.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Adtd.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore>(new SettingsStore());
        services.AddSingleton<IDialogs, WpfDialogs>();
        services.AddSingleton(sp => new Session(sp.GetRequiredService<ISettingsStore>()));
        services.AddSingleton(sp => new ConnectViewModel(sp.GetRequiredService<Session>(), sp.GetRequiredService<IDialogs>()));
        services.AddSingleton<DiagramsViewModel>();
        services.AddSingleton<StyleViewModel>();
        services.AddSingleton<PreviewViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<ConnectPage>();
        services.AddSingleton<DiagramsPage>();
        services.AddSingleton<StylePage>();
        services.AddSingleton<PreviewPage>();
        services.AddSingleton<ExportPage>();
        services.AddSingleton<AboutPage>();
        services.AddSingleton<MainWindow>();
        Services = services.BuildServiceProvider();

        Restore();
        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    /// <summary>Picks up where the last session left off: the icon, the template and layout, and the forest.</summary>
    static void Restore()
    {
        var session = Services.GetRequiredService<Session>();
        var s = session.Settings;
        if (s.IconPath is { } icon && File.Exists(icon)) Services.GetRequiredService<StyleViewModel>().OpenIcon(icon);
        if (s.TemplatePath is { } template && File.Exists(template)) Services.GetRequiredService<StyleViewModel>().OpenTemplate(template);
        if (s.SnapshotPath is { } snapshot && File.Exists(snapshot)) Services.GetRequiredService<ConnectViewModel>().OpenSnapshotFile(snapshot);
    }
}
