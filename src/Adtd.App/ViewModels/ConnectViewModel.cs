using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Adtd.App.Services;
using Adtd.Collect;
using Adtd.Model;
using Adtd.Model.Samples;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Adtd.App.ViewModels;

public sealed record LogLine(CollectLevel Level, string Text);

/// <summary>
/// The Forest page: collect a forest over LDAP (or open a snapshot, or a sample), and see what's in it.
/// The password is held only for the collection and never saved.
/// </summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    readonly Session _session;
    readonly IDialogs _dialogs;
    readonly Func<ConnectionOptions, IDirectoryFactory> _directories;
    readonly Func<IIpResolver> _resolver;
    readonly string _logFolder;
    CancellationTokenSource? _cts;

    public const string SnapshotFilter = "Forest snapshots (*.json)|*.json|All files (*.*)|*.*";
    public const string CsvFilter = "CSV files (*.csv)|*.csv";

    public ConnectViewModel(Session session, IDialogs dialogs, Func<ConnectionOptions, IDirectoryFactory>? directories = null, Func<IIpResolver>? resolver = null,
                            string? logFolder = null)
    {
        _logFolder = logFolder ?? Path.Combine(SettingsStore.Folder, "logs");
        _session = session;
        _dialogs = dialogs;
        _directories = directories ?? (o => new LdapDirectoryFactory(o));
        _resolver = resolver ?? (() => new DnsIpResolver());
        _server = session.Settings.Server ?? "";
        _user = session.Settings.User ?? "";
        _useOtherCredentials = !string.IsNullOrEmpty(session.Settings.User);
        _resolveAddresses = session.Settings.ResolveAddresses;
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Session.Snapshot)) Refresh(); };
        Refresh();
    }

    // ---- connection --------------------------------------------------------------------------------

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CollectCommand), nameof(ValidateCommand))] string _server;
    [ObservableProperty] bool _useOtherCredentials;
    [ObservableProperty] string _user;
    [ObservableProperty] bool _resolveAddresses;
    /// <summary>Set by the view from its PasswordBox; never saved.</summary>
    public string Password { get; set; } = "";

    /// <summary>"This computer is in contoso.com" when the locator finds a DC.</summary>
    [ObservableProperty] string? _locatedText;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CollectCommand), nameof(ValidateCommand), nameof(CancelCommand))] bool _isBusy;
    [ObservableProperty] string? _status;
    [ObservableProperty] bool _statusIsError;
    public ObservableCollection<LogLine> Log { get; } = [];

    public IReadOnlyList<string> Samples => SampleForests.Names;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SampleDescription))] string _selectedSample = SampleForests.Names[1];
    /// <summary>What the chosen sample covers.</summary>
    public string SampleDescription => SampleForests.Descriptions.GetValueOrDefault(SelectedSample, "");

    partial void OnServerChanged(string value) => _session.Remember(s => s with { Server = value });
    partial void OnUserChanged(string value) => _session.Remember(s => s with { User = UseOtherCredentials ? value : null });
    partial void OnUseOtherCredentialsChanged(bool value) => _session.Remember(s => s with { User = value ? User : null });
    partial void OnResolveAddressesChanged(bool value) => _session.Remember(s => s with { ResolveAddresses = value });

    /// <summary>Asks Windows which domain this computer is in (DsGetDcName), and fills in the server if it's empty.</summary>
    [RelayCommand]
    public async Task Locate()
    {
        var located = await Task.Run(() => { try { return DcLocator.Find(); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; } });
        if (located == null) { LocatedText = Strings.Get("Connect_NotInDomain"); return; }
        LocatedText = Strings.F("Connect_InDomain", located.Domain, located.DomainController);
        if (string.IsNullOrWhiteSpace(Server)) Server = located.Domain;
    }

    bool CanConnect() => !IsBusy && !string.IsNullOrWhiteSpace(Server);

    NetworkCredential? Credential()
    {
        if (!UseOtherCredentials || string.IsNullOrWhiteSpace(User)) return null;
        var user = User.Trim();
        if (user.Contains('\\')) { var p = user.Split('\\', 2); return new NetworkCredential(p[1], Password, p[0]); }
        return new NetworkCredential(user, Password);
    }

    /// <summary>Binds to the server and reads its RootDSE: proves the name, the network and the credentials.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    public async Task Validate()
    {
        IsBusy = true;
        SetStatus(Strings.F("Connect_Validating", Server.Trim()), false);
        try
        {
            var target = Server.Trim();
            var factory = _directories(new ConnectionOptions(Credential(), TimeSpan.FromSeconds(15)));
            var dc = await Task.Run(() =>
            {
                using var d = factory.Connect(target);
                var dse = d.Read("", "dnsHostName", "rootDomainNamingContext");
                return dse?.Get("dnsHostName") ?? d.Server;
            });
            SetStatus(Strings.F("Connect_Validated", dc), false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { SetStatus(ex.Message, true); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    public async Task Collect()
    {
        IsBusy = true;
        Log.Clear();
        _cts = new CancellationTokenSource();
        var target = Server.Trim();
        SetStatus(Strings.F("Connect_Collecting", target), false);
        var logPath = Path.Combine(_logFolder, $"collect-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        StreamWriter? file = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            file = new StreamWriter(logPath) { AutoFlush = true };
            file.WriteLine($"ADTD for PowerPoint {ForestReader.Version} collect from {target}{(UseOtherCredentials ? $" as {User}" : "")}");
            file.WriteLine($"Started {DateTimeOffset.Now:O} on {Environment.MachineName} as {Environment.UserDomainName}\\{Environment.UserName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { file = null; }

        // The file gets every event as it happens, in order; the on-screen log gets the steps and warnings on the UI thread.
        var screen = new Progress<CollectEvent>(e => { if (e.Level != CollectLevel.Detail) Log.Add(new LogLine(e.Level, e.Message)); });
        var gate = new object();
        var progress = new Immediate<CollectEvent>(e =>
        {
            lock (gate) file?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {e.Level,-7} {e.Message}");
            ((IProgress<CollectEvent>)screen).Report(e);
        });
        try
        {
            var reader = new ForestReader(_directories(new ConnectionOptions(Credential())), ResolveAddresses ? _resolver() : null, progress);
            var options = new CollectOptions { ResolveAddresses = ResolveAddresses };
            var ct = _cts.Token;
            var snapshot = await Task.Run(() => reader.ReadAsync(target, options, ct), ct);
            _session.SetSnapshot(snapshot, Strings.F("Connect_SourceServer", snapshot.CollectedFrom));
            SetStatus(Strings.F("Connect_Collected", snapshot.ForestName), false);
        }
        catch (OperationCanceledException) { file?.WriteLine("Cancelled."); SetStatus(Strings.Get("Connect_Cancelled"), true); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            file?.WriteLine($"FAILED: {ex}");
            SetStatus(file != null ? Strings.F("Connect_FailedWithLog", ex.Message, logPath) : ex.Message, true);
        }
        finally
        {
            lock (gate) { file?.Dispose(); file = null; }
            _cts.Dispose();
            _cts = null;
            Password = "";
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    void Cancel() => _cts?.Cancel();

    // ---- snapshots ---------------------------------------------------------------------------------

    [RelayCommand]
    public void OpenSnapshot()
    {
        if (_dialogs.AskOpen(SnapshotFilter, _session.Settings.SnapshotPath) is not { } path) return;
        OpenSnapshotFile(path);
    }

    public bool OpenSnapshotFile(string path)
    {
        try
        {
            var s = SnapshotJson.Load(path);
            _session.SetSnapshot(s, Path.GetFileName(path), path);
            SetStatus(Strings.F("Connect_Opened", s.ForestName, Path.GetFileName(path)), false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException)
        {
            SetStatus(Strings.F("Connect_CantOpen", Path.GetFileName(path), ex.Message), true);
            return false;
        }
    }

    [RelayCommand]
    public void LoadSample()
    {
        _session.SetSnapshot(SampleForests.ByName(SelectedSample), Strings.F("Connect_SourceSample", SelectedSample));
        SetStatus(Strings.F("Connect_SampleLoaded", SelectedSample), false);
    }

    [RelayCommand(CanExecute = nameof(HasSnapshot))]
    public void SaveSnapshot()
    {
        if (_session.Snapshot is not { } s) return;
        var suggested = Path.Combine(Folder(), $"adtd-{s.ForestName}-{s.CollectedAt.LocalDateTime:yyyyMMdd-HHmm}.json");
        if (_dialogs.AskSave(SnapshotFilter, suggested) is not { } path) return;
        try
        {
            SnapshotJson.Save(s, path);
            _session.Remember(x => x with { SnapshotPath = path });
            SetStatus(Strings.F("Connect_Saved", path), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus(ex.Message, true); }
    }

    /// <summary>The domain controllers as a CSV table, for a spreadsheet.</summary>
    [RelayCommand(CanExecute = nameof(HasSnapshot))]
    public void ExportCsv()
    {
        if (_session.Snapshot is not { } s) return;
        if (_dialogs.AskSave(CsvFilter, Path.Combine(Folder(), $"{s.ForestName} domain controllers.csv")) is not { } path) return;
        try
        {
            File.WriteAllText(path, Csv(s), new UTF8Encoding(true));
            SetStatus(Strings.F("Connect_Saved", path), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus(ex.Message, true); }
    }

    public static string Csv(ForestSnapshot s)
    {
        var sb = new StringBuilder();
        static string Q(string? v) => v == null ? "" : v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        sb.AppendLine("Name,DNS name,Domain,Site,Global catalog,Read-only,Operating system,IPv4,Roles");
        foreach (var dc in s.DomainControllers.OrderBy(d => d.Domain).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var roles = new List<string>();
            var d = s.Domains.FirstOrDefault(x => string.Equals(x.DnsName, dc.Domain, StringComparison.OrdinalIgnoreCase));
            if (d?.PdcMaster == dc.Key) roles.Add("PDC");
            if (d?.RidMaster == dc.Key) roles.Add("RID");
            if (d?.InfrastructureMaster == dc.Key) roles.Add("Infrastructure");
            if (s.SchemaMaster == dc.Key) roles.Add("Schema");
            if (s.DomainNamingMaster == dc.Key) roles.Add("Domain naming");
            if (s.Sites.Any(x => x.Istg == dc.Key)) roles.Add("ISTG");
            sb.AppendLine(string.Join(",", Q(dc.Name), Q(dc.DnsHostName), Q(dc.Domain), Q(dc.Site), dc.IsGlobalCatalog ? "Yes" : "No",
                dc.IsReadOnly ? "Yes" : "No", Q(dc.OperatingSystem), Q(string.Join(" ", dc.IPv4)), Q(string.Join(" ", roles))));
        }
        return sb.ToString();
    }

    string Folder() => Path.GetDirectoryName(_session.Settings.SnapshotPath) is { Length: > 0 } d && Directory.Exists(d)
        ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    // ---- what's in the forest ----------------------------------------------------------------------

    public bool HasSnapshot => _session.Snapshot != null;
    [ObservableProperty] string? _forestName;
    [ObservableProperty] string? _source;
    [ObservableProperty] IReadOnlyList<CountCard> _counts = [];
    [ObservableProperty] IReadOnlyList<string> _warnings = [];

    void Refresh()
    {
        var s = _session.Snapshot;
        OnPropertyChanged(nameof(HasSnapshot));
        SaveSnapshotCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        ForestName = s?.ForestName;
        Source = s == null ? null : Strings.F("Connect_SourceLine", _session.SnapshotSource, s.CollectedAt.LocalDateTime);
        Counts = s == null ? [] :
        [
            new(Strings.Get("Count_Domains"), s.Domains.Count),
            new(Strings.Get("Count_Dcs"), s.DomainControllers.Count),
            new(Strings.Get("Count_Sites"), s.Sites.Count),
            new(Strings.Get("Count_Subnets"), s.Subnets.Count),
            new(Strings.Get("Count_SiteLinks"), s.SiteLinks.Count),
            new(Strings.Get("Count_Trusts"), s.Domains.Sum(d => d.Trusts.Count(t => t.Kind is not (TrustKind.ParentChild or TrustKind.TreeRoot)))),
        ];
        Warnings = s?.Warnings ?? [];
    }

    void SetStatus(string text, bool error) { Status = text; StatusIsError = error; }
}

public sealed record CountCard(string Label, int Value);

/// <summary>An <see cref="IProgress{T}"/> that acts at once on the reporting thread (Progress&lt;T&gt; posts, and can reorder).</summary>
sealed class Immediate<T>(Action<T> act) : IProgress<T>
{
    public void Report(T value) => act(value);
}
