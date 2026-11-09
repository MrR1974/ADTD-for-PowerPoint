using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using Adtd.App.Preview;
using Adtd.App.Services;
using Adtd.Imaging;
using Adtd.Layout;
using Adtd.PowerPoint;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Adtd.App.ViewModels;

public sealed record LayoutChoice(LayoutInfo Layout, string Display)
{
    public override string ToString() => Display;
}

public sealed record Swatch(string Name, Brush Brush, string Hex);

/// <summary>The Style page: the template, the layout the diagrams go on, the theme it brings, and the DC icon.</summary>
public sealed partial class StyleViewModel : ObservableObject
{
    readonly Session _session;
    readonly IDialogs _dialogs;
    bool _syncing;

    public const string TemplateFilter = "PowerPoint templates and presentations (*.potx;*.pptx)|*.potx;*.pptx|All files (*.*)|*.*";
    public const string IconFilter = "Icons (*.svg;*.png)|*.svg;*.png|All files (*.*)|*.*";

    public StyleViewModel(Session session, IDialogs dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        session.PropertyChanged += OnSession;
        SyncTemplate();
        SyncIcon();
    }

    // ---- template and layout -----------------------------------------------------------------------

    [ObservableProperty] string? _templateName;
    [ObservableProperty] string? _templateDetails;
    [ObservableProperty] string? _templateError;
    [ObservableProperty] IReadOnlyList<LayoutChoice> _layouts = [];
    [ObservableProperty] LayoutChoice? _selectedLayout;
    [ObservableProperty] ImageSource? _wireframe;
    [ObservableProperty] string? _contentSource;
    [ObservableProperty] string? _sectionLayoutText;
    [ObservableProperty] IReadOnlyList<Swatch> _swatches = [];
    [ObservableProperty] string? _fontsText;
    public bool HasTemplate => _session.Template != null;
    public bool HasExistingSlides => _session.Template?.ExistingSlides > 0;
    public string ExistingSlidesText => Strings.F("Style_KeepSlides", _session.Template?.ExistingSlides ?? 0);

    public bool KeepExistingSlides
    {
        get => _session.KeepExistingSlides;
        set { _session.KeepExistingSlides = value; OnPropertyChanged(); }
    }

    [RelayCommand]
    public void BrowseTemplate()
    {
        if (_dialogs.AskOpen(TemplateFilter, _session.TemplatePath) is { } path) OpenTemplate(path);
    }

    public bool OpenTemplate(string path)
    {
        try
        {
            _session.LoadTemplate(path);
            TemplateError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            TemplateError = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    void ClearTemplate() => _session.ClearTemplate();

    partial void OnSelectedLayoutChanged(LayoutChoice? value)
    {
        if (_syncing || value == null) return;
        _session.Layout = value.Layout;
    }

    void SyncTemplate()
    {
        _syncing = true;
        try
        {
            var t = _session.Template;
            OnPropertyChanged(nameof(HasTemplate));
            OnPropertyChanged(nameof(HasExistingSlides));
            OnPropertyChanged(nameof(ExistingSlidesText));
            OnPropertyChanged(nameof(KeepExistingSlides));
            if (t == null)
            {
                TemplateName = null;
                TemplateDetails = null;
                Layouts = [];
                SelectedLayout = null;
                Wireframe = null;
                ContentSource = null;
                SectionLayoutText = null;
                SetTheme(ThemeInfo.OfficeDefault);
                return;
            }
            TemplateName = Path.GetFileName(_session.TemplatePath);
            TemplateDetails = Strings.F("Style_TemplateDetails", t.SlideWidth / Emu.PerInch, t.SlideHeight / Emu.PerInch, t.Layouts.Count, t.Themes.Count);
            var multi = t.Themes.Count > 1;
            Layouts = t.Layouts.Select(l => new LayoutChoice(l, multi ? Strings.F("Style_LayoutOnMaster", l.Name, l.MasterIndex + 1) : l.Name)).ToList();
            SelectedLayout = Layouts.FirstOrDefault(c => c.Layout == _session.Layout);
            SyncLayout();
        }
        finally { _syncing = false; }
    }

    void SyncLayout()
    {
        var t = _session.Template;
        var l = _session.Layout;
        if (t == null || l == null) return;
        Wireframe = Preview.Wireframe.Render(t, l);
        ContentSource = Strings.F("Style_ContentSource", l.Content.W / Emu.PerInch, l.Content.H / Emu.PerInch, l.ContentSource);
        var section = t.SectionLayoutFor(l);
        SectionLayoutText = section != null ? Strings.F("Style_SectionLayout", section.Name) : Strings.Get("Style_NoSectionLayout");
        SetTheme(t.ThemeOf(l));
        if (!_syncing)
        {
            _syncing = true;
            SelectedLayout = Layouts.FirstOrDefault(c => c.Layout == l);
            _syncing = false;
        }
    }

    void SetTheme(ThemeInfo theme)
    {
        SchemeSlot[] slots =
        [
            SchemeSlot.Tx1, SchemeSlot.Bg1, SchemeSlot.Tx2, SchemeSlot.Bg2,
            SchemeSlot.Accent1, SchemeSlot.Accent2, SchemeSlot.Accent3, SchemeSlot.Accent4, SchemeSlot.Accent5, SchemeSlot.Accent6,
        ];
        Swatches = slots.Select(s => new Swatch(Strings.Get($"Slot_{s}"), PlanRenderer.Brush(theme, new ThemeColor(s)), "#" + theme.Resolve(new ThemeColor(s)))).ToList();
        FontsText = Strings.F("Style_Fonts", theme.MajorFont, theme.MinorFont);
    }

    // ---- icon --------------------------------------------------------------------------------------

    [ObservableProperty] ImageSource? _iconImage;
    [ObservableProperty] string? _iconName;
    [ObservableProperty] IReadOnlyList<string> _iconWarnings = [];
    [ObservableProperty] string? _iconError;
    public bool IsDefaultIcon => _session.IconPath == null;

    [RelayCommand]
    public void BrowseIcon()
    {
        if (_dialogs.AskOpen(IconFilter, _session.IconPath) is { } path) OpenIcon(path);
    }

    public bool OpenIcon(string? path)
    {
        try
        {
            _session.LoadIcon(path);
            IconError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            IconError = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    void DefaultIcon() => OpenIcon(null);

    void SyncIcon()
    {
        var icon = _session.Icon;
        IconImage = PlanRenderer.Bitmap(icon.Png);
        IconName = _session.IconPath == null ? Strings.F("Style_DefaultIcon", icon.Name) : icon.Name;
        IconWarnings = icon.Warnings;
        OnPropertyChanged(nameof(IsDefaultIcon));
    }

    void OnSession(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Session.Template): SyncTemplate(); break;
            case nameof(Session.Layout): SyncLayout(); break;
            case nameof(Session.Icon): SyncIcon(); break;
            case nameof(Session.KeepExistingSlides): OnPropertyChanged(nameof(KeepExistingSlides)); break;
        }
    }
}
