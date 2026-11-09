using System.ComponentModel;
using System.Windows.Media;
using Adtd.App.Preview;
using Adtd.Layout;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Adtd.App.ViewModels;

public sealed record SlideEntry(int Number, string Title, string Section, SlidePlan Slide)
{
    public string Display => $"{Number}. {Title}";
}

/// <summary>The Preview page: the planned slides by section, the chosen one drawn, and whatever the planner warned about.</summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    readonly Session _session;

    public PreviewViewModel(Session session)
    {
        _session = session;
        session.PropertyChanged += OnSession;
        Sync();
    }

    [ObservableProperty] IReadOnlyList<SlideEntry> _slides = [];
    [ObservableProperty] SlideEntry? _selected;
    [ObservableProperty] ImageSource? _image;
    [ObservableProperty] IReadOnlyList<string> _warnings = [];
    [ObservableProperty] IReadOnlyList<string> _problems = [];
    [ObservableProperty] string? _summary;
    [ObservableProperty] double _slideAspect = 16.0 / 9;
    public bool IsPlanning => _session.IsPlanning;
    public bool HasSlides => Slides.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
    public bool HasProblems => Problems.Count > 0;
    public string? PlanError => _session.PlanError;

    partial void OnSelectedChanged(SlideEntry? value) => Draw();
    partial void OnSlidesChanged(IReadOnlyList<SlideEntry> value) => OnPropertyChanged(nameof(HasSlides));
    partial void OnWarningsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasWarnings));
    partial void OnProblemsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasProblems));

    void Draw()
    {
        Image = Selected is { } s
            ? PlanRenderer.Render(s.Slide, new PlanRenderer.Context(_session.PlanFrame, _session.PlanTheme, _session.Icon.Png))
            : null;
    }

    void Sync()
    {
        var plan = _session.Plan;
        var keep = Selected?.Number;
        Slides = plan?.Slides.Select((s, i) => new SlideEntry(i + 1, s.Title, s.Section ?? "", s)).ToList() ?? [];
        Warnings = plan?.Warnings ?? [];
        Problems = _session.Problems;
        SlideAspect = _session.PlanFrame.Width / _session.PlanFrame.Height;
        Summary = plan == null ? (_session.Snapshot == null ? Strings.Get("Preview_NoForest") : null)
            : Strings.F("Preview_Summary", plan.Slides.Count, _session.Template == null ? Strings.Get("Preview_NoTemplate") : _session.Layout?.Name);
        Selected = Slides.FirstOrDefault(s => s.Number == keep) ?? Slides.FirstOrDefault();
        Draw();
    }

    void OnSession(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Session.Plan): Sync(); break;
            case nameof(Session.IsPlanning): OnPropertyChanged(nameof(IsPlanning)); break;
            case nameof(Session.PlanError): OnPropertyChanged(nameof(PlanError)); break;
            case nameof(Session.Snapshot) when _session.Snapshot == null: Sync(); break;
        }
    }
}
