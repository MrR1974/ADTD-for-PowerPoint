using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Adtd.App.Services;
using Adtd.PowerPoint;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Adtd.App.ViewModels;

public enum ReportLevel { Success, Info, Warning, Error }

public sealed record ReportLine(ReportLevel Level, string Text);

/// <summary>The Export page: where to save the deck, writing it, and what came of it.</summary>
public sealed partial class ExportViewModel : ObservableObject
{
    readonly Session _session;
    readonly IDialogs _dialogs;

    public const string DeckFilter = "PowerPoint presentation (*.pptx)|*.pptx";

    public ExportViewModel(Session session, IDialogs dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        _openWhenDone = session.Settings.OpenWhenDone;
        session.PropertyChanged += OnSession;
        Suggest();
    }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ExportCommand))] string _outputPath = "";
    [ObservableProperty] bool _openWhenDone;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ExportCommand))] bool _isExporting;
    [ObservableProperty] string? _lastOutput;
    public ObservableCollection<ReportLine> Report { get; } = [];

    /// <summary>Why Export is unavailable, or null when it's ready.</summary>
    public string? Blocker =>
        _session.Snapshot == null ? Strings.Get("Export_NeedForest")
        : _session.Template == null || _session.Layout == null ? Strings.Get("Export_NeedTemplate")
        : _session.IsPlanning || _session.Plan == null ? (_session.PlanError ?? Strings.Get("Export_Planning"))
        : null;

    partial void OnOpenWhenDoneChanged(bool value) => _session.Remember(s => s with { OpenWhenDone = value });

    bool _userChosePath;

    void Suggest()
    {
        if (_userChosePath || _session.Snapshot is not { } s) return;
        var folder = _session.Settings.OutputFolder is { } f && Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        OutputPath = Path.Combine(folder, $"{s.ForestName} topology.pptx");
    }

    [RelayCommand]
    void Browse()
    {
        if (_dialogs.AskSave(DeckFilter, string.IsNullOrWhiteSpace(OutputPath) ? "topology.pptx" : OutputPath) is not { } path) return;
        _userChosePath = true;
        OutputPath = path;
    }

    bool CanExport() => !IsExporting && Blocker == null && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task Export()
    {
        if (_session.Plan is not { } plan || _session.Template == null || _session.Layout is not { } layout || _session.TemplatePath is not { } templatePath) return;
        IsExporting = true;
        Report.Clear();
        var path = Path.GetFullPath(OutputPath.Trim());
        if (!path.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)) path += ".pptx";
        var icon = _session.Icon;
        var options = new ComposeOptions { KeepExistingSlides = _session.KeepExistingSlides, SectionLayout = _session.SectionLayout };
        var problems = _session.Problems;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var report = await Task.Run(() => PptxComposer.Write(path, templatePath, layout, plan, new IconImage(icon.Svg, icon.Png), options));
            LastOutput = path;
            _session.Remember(s => s with { OutputFolder = Path.GetDirectoryName(path) });
            Report.Add(new(report.ValidationErrors.Count == 0 ? ReportLevel.Success : ReportLevel.Error,
                Strings.F(report.ValidationErrors.Count == 0 ? "Export_Saved" : "Export_SavedInvalid", report.Slides, path)));
            foreach (var e in report.ValidationErrors) Report.Add(new(ReportLevel.Error, Strings.F("Export_Invalid", e)));
            foreach (var p in problems) Report.Add(new(ReportLevel.Error, Strings.F("Export_Problem", p)));
            foreach (var w in report.Warnings.Concat(icon.Warnings).Distinct()) Report.Add(new(ReportLevel.Warning, w));
            if (report.ValidationErrors.Count == 0 && problems.Count == 0 && OpenWhenDone) _dialogs.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Report.Add(new(ReportLevel.Error, Strings.F("Export_Failed", ex.Message)));
        }
        finally { IsExporting = false; }
    }

    [RelayCommand]
    void OpenOutput() { if (LastOutput != null && File.Exists(LastOutput)) _dialogs.Open(LastOutput); }

    [RelayCommand]
    void ShowOutput() { if (LastOutput != null && File.Exists(LastOutput)) _dialogs.ShowInFolder(LastOutput); }

    void OnSession(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Session.Snapshot)) Suggest();
        if (e.PropertyName is nameof(Session.Snapshot) or nameof(Session.Template) or nameof(Session.Layout) or nameof(Session.Plan)
            or nameof(Session.IsPlanning) or nameof(Session.PlanError))
        {
            OnPropertyChanged(nameof(Blocker));
            ExportCommand.NotifyCanExecuteChanged();
        }
    }
}
