using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adtd.Layout;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Adtd.App.ViewModels;

/// <summary>The Diagrams page: which slides to make and what goes on them. Every switch is a <see cref="DeckOptions"/> field.</summary>
public sealed partial class DiagramsViewModel : ObservableObject
{
    readonly Session _session;

    public DiagramsViewModel(Session session)
    {
        _session = session;
        session.PropertyChanged += OnSession;
        UpdateSummary();
    }

    DeckOptions O => _session.Options;

    void Set(Func<DeckOptions, DeckOptions> change, [CallerMemberName] string? name = null)
    {
        var next = change(O);
        if (next == O) return;
        _session.Options = next;
        OnPropertyChanged(name);
    }

    // Slides
    public bool Summary { get => O.Summary; set => Set(o => o with { Summary = value }); }
    public bool DomainsOverview { get => O.DomainsOverview; set => Set(o => o with { DomainsOverview = value }); }
    public bool DomainDetails { get => O.DomainDetails; set => Set(o => o with { DomainDetails = value }); }
    public bool SitesOverview
    {
        get => O.SitesOverview;
        set { Set(o => o with { SitesOverview = value }); OnPropertyChanged(nameof(CanConsolidate)); }
    }
    public bool SiteDetails { get => O.SiteDetails; set => Set(o => o with { SiteDetails = value }); }
    public bool ForestSectionHeader { get => O.ForestSectionHeader; set => Set(o => o with { ForestSectionHeader = value }); }
    public bool SitesSectionHeader { get => O.SitesSectionHeader; set => Set(o => o with { SitesSectionHeader = value }); }

    // Content
    public bool OtherTrusts { get => O.OtherTrusts; set => Set(o => o with { OtherTrusts = value }); }
    public bool LinkLabels { get => O.LinkLabels; set => Set(o => o with { LinkLabels = value }); }
    public bool HideEmptySites
    {
        get => O.HideEmptySites;
        set { Set(o => o with { HideEmptySites = value }); OnPropertyChanged(nameof(CanConsolidate)); }
    }
    public bool ConsolidateEmptySites { get => O.ConsolidateEmptySites; set => Set(o => o with { ConsolidateEmptySites = value }); }
    /// <summary>Consolidating empty sites means nothing once they're left out.</summary>
    public bool CanConsolidate => O.SitesOverview && !O.HideEmptySites;
    public bool Subnets { get => O.Subnets; set => Set(o => o with { Subnets = value }); }
    public bool Addresses { get => O.Addresses; set => Set(o => o with { Addresses = value }); }
    public bool Replication { get => O.Replication; set => Set(o => o with { Replication = value }); }
    public bool Legend { get => O.Legend; set => Set(o => o with { Legend = value }); }

    /// <summary>More DCs than this on a slide go onto "(cont.)" slides.</summary>
    public double MaxDcsPerSlide
    {
        get => O.MaxDcsPerSlide;
        set => Set(o => o with { MaxDcsPerSlide = Math.Clamp((int)Math.Round(double.IsNaN(value) ? 40 : value), 1, 500) });
    }

    /// <summary>The smallest text on any slide, in points.</summary>
    public double MinFontPt
    {
        get => O.MinFontPt;
        set => Set(o => o with { MinFontPt = Math.Clamp(Math.Round((double.IsNaN(value) ? 8 : value) * 2) / 2, 6, 14) });
    }

    /// <summary>Back to the defaults.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    void Reset()
    {
        _session.Options = new DeckOptions();
        OnPropertyChanged(string.Empty);
    }

    [ObservableProperty] string? _summaryText;

    void OnSession(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Session.Plan) or nameof(Session.IsPlanning) or nameof(Session.Snapshot)) UpdateSummary();
    }

    void UpdateSummary() => SummaryText =
        _session.Snapshot == null ? Strings.Get("Diagrams_NoForest")
        : _session.IsPlanning ? Strings.Get("Diagrams_Planning")
        : _session.Plan is { } p ? Strings.F("Diagrams_SlideCount", p.Slides.Count,
              p.Slides.Count(s => s.Section == DeckPlanner.ForestSection), p.Slides.Count(s => s.Section == DeckPlanner.SitesSection))
        : _session.PlanError;
}
