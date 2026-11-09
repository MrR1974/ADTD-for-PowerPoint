using System.IO;
using Adtd.App.Services;
using Adtd.Imaging;
using Adtd.Layout;
using Adtd.Model;
using Adtd.PowerPoint;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Adtd.App.ViewModels;

/// <summary>
/// Everything the pages share: the forest, the template and layout, the icon and the diagram options, and the deck
/// planned from them. Any change to an input plans the deck again in the background; a newer change discards an
/// older plan still in progress. The preview and the export both use <see cref="Plan"/>, so what's previewed is what's
/// written.
/// </summary>
public sealed partial class Session : ObservableObject
{
    readonly ISettingsStore _store;
    int _version;

    public Session(ISettingsStore store, TimeSpan? debounce = null)
    {
        _store = store;
        Debounce = debounce ?? TimeSpan.FromMilliseconds(250);
        Settings = store.Load();
        _options = Settings.Options;
        _keepExistingSlides = Settings.KeepExistingSlides;
        _icon = IconAsset.Default();
    }

    /// <summary>How long to wait for further changes before planning.</summary>
    public TimeSpan Debounce { get; }

    // ---- settings ----------------------------------------------------------------------------------

    public AppSettings Settings { get; private set; }

    /// <summary>Changes the saved settings and saves them.</summary>
    public void Remember(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        _store.Save(Settings);
    }

    // ---- inputs -----------------------------------------------------------------------------------

    [ObservableProperty] ForestSnapshot? _snapshot;
    /// <summary>Where the forest came from, for display: a file, a sample or a server.</summary>
    [ObservableProperty] string? _snapshotSource;

    [ObservableProperty] TemplateInfo? _template;
    [ObservableProperty] string? _templatePath;
    [ObservableProperty] LayoutInfo? _layout;

    [ObservableProperty] IconAsset _icon;
    /// <summary>The icon file, or null for the built-in icon.</summary>
    [ObservableProperty] string? _iconPath;

    [ObservableProperty] DeckOptions _options;
    [ObservableProperty] bool _keepExistingSlides;

    public void SetSnapshot(ForestSnapshot snapshot, string source, string? path = null)
    {
        SnapshotSource = source;
        Snapshot = snapshot;
        Remember(s => s with { SnapshotPath = path });
    }

    /// <summary>Reads a template and picks its layout: the one used last time if it has it, else its default. Throws <see cref="InvalidDataException"/> or <see cref="IOException"/>.</summary>
    public void LoadTemplate(string path)
    {
        var t = TemplateReader.Read(path);
        var layout = (Settings.TemplatePath == path && Settings.LayoutName != null ? t.Find(Settings.LayoutName, Settings.LayoutMaster) : null) ?? t.DefaultLayout;
        Layout = null;   // so nothing plans against the old layout with the new template
        TemplatePath = path;
        Template = t;
        Layout = layout;
        if (t.ExistingSlides == 0) KeepExistingSlides = false;
        Remember(s => s with { TemplatePath = path, LayoutName = layout.Name, LayoutMaster = layout.MasterIndex });
    }

    public void ClearTemplate()
    {
        Layout = null;
        Template = null;
        TemplatePath = null;
        KeepExistingSlides = false;
        Remember(s => s with { TemplatePath = null, LayoutName = null });
    }

    /// <summary>Uses an icon file, or the built-in icon for null. Throws <see cref="InvalidDataException"/> or <see cref="IOException"/>.</summary>
    public void LoadIcon(string? path)
    {
        Icon = path == null ? IconAsset.Default() : IconAsset.Load(path);
        IconPath = path;
        Remember(s => s with { IconPath = path });
    }

    partial void OnSnapshotChanged(ForestSnapshot? value) => Replan();
    partial void OnTemplateChanged(TemplateInfo? value) => Replan();
    partial void OnIconChanged(IconAsset value) => Replan();
    partial void OnOptionsChanged(DeckOptions value) { Remember(s => s with { Options = value }); Replan(); }
    partial void OnKeepExistingSlidesChanged(bool value) => Remember(s => s with { KeepExistingSlides = value });
    partial void OnLayoutChanged(LayoutInfo? value)
    {
        if (value != null) Remember(s => s with { LayoutName = value.Name, LayoutMaster = value.MasterIndex });
        Replan();
    }

    // ---- what the deck is planned against -----------------------------------------------------------

    /// <summary>
    /// With no template yet: a 13.33 × 7.5 in slide with the title across the top, so there's something to preview.
    /// </summary>
    public static readonly SlideFrame DefaultFrame = new(Emu.Inches(13.333), Emu.Inches(7.5),
        new Rect(Emu.Inches(0.5), Emu.Inches(1.5), Emu.Inches(12.333), Emu.Inches(5.5)),
        new Rect(Emu.Inches(0.5), Emu.Inches(0.3), Emu.Inches(12.333), Emu.Inches(1)));

    public SlideFrame Frame => Template != null && Layout != null ? Template.FrameOf(Layout) : DefaultFrame;
    public ThemeInfo Theme => Template != null && Layout != null ? Template.ThemeOf(Layout) : ThemeInfo.OfficeDefault;
    public LayoutInfo? SectionLayout => Template != null && Layout != null ? Template.SectionLayoutFor(Layout) : null;

    // ---- the plan ----------------------------------------------------------------------------------

    [ObservableProperty] DeckPlan? _plan;
    /// <summary>The frame and theme <see cref="Plan"/> was made for.</summary>
    [ObservableProperty] SlideFrame _planFrame = DefaultFrame;
    [ObservableProperty] ThemeInfo _planTheme = ThemeInfo.OfficeDefault;
    /// <summary>Layout rules the plan breaks (<see cref="PlanChecks"/>); empty when it's sound.</summary>
    [ObservableProperty] IReadOnlyList<string> _problems = [];
    [ObservableProperty] bool _isPlanning;
    [ObservableProperty] string? _planError;

    /// <summary>The planning in progress (or the last one). Tests await it.</summary>
    public Task Planning { get; private set; } = Task.CompletedTask;

    public void Replan()
    {
        var version = ++_version;
        if (Snapshot is not { } forest)
        {
            Plan = null;
            Problems = [];
            PlanError = null;
            IsPlanning = false;
            Planning = Task.CompletedTask;
            return;
        }
        IsPlanning = true;
        Planning = Run(version, forest, Frame, Theme, Icon.Aspect, Options);
    }

    async Task Run(int version, ForestSnapshot forest, SlideFrame frame, ThemeInfo theme, double aspect, DeckOptions options)
    {
        try
        {
            if (Debounce > TimeSpan.Zero) await Task.Delay(Debounce);
            if (version != _version) return;
            var (plan, problems) = await Task.Run(() =>
            {
                using var measurer = new SkiaTextMeasurer(theme);
                var p = DeckPlanner.Plan(forest, frame, theme, measurer, aspect, options);
                return (p, PlanChecks.Check(p, frame, measurer, options.MinFontPt));
            });
            if (version != _version) return;
            PlanFrame = frame;
            PlanTheme = theme;
            Problems = problems;
            PlanError = null;
            Plan = plan;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (version != _version) return;
            Plan = null;
            Problems = [];
            PlanError = ex.Message;
        }
        finally
        {
            if (version == _version) IsPlanning = false;
        }
    }
}
