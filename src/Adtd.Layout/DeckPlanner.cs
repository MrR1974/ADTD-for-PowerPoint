using System.Net;
using Adtd.Model;

namespace Adtd.Layout;

/// <summary>Which slides to make and what to show on them.</summary>
public sealed record DeckOptions
{
    public bool Summary { get; init; } = true;
    public bool DomainsOverview { get; init; } = true;
    public bool SitesOverview { get; init; } = true;
    public bool DomainDetails { get; init; } = true;
    public bool SiteDetails { get; init; } = true;

    /// <summary>Subnet tables on the site slides.</summary>
    public bool Subnets { get; init; } = true;
    /// <summary>DC IPv4 addresses on the cards.</summary>
    public bool Addresses { get; init; } = true;
    /// <summary>Replication connections between the DCs on each site slide.</summary>
    public bool Replication { get; init; }
    /// <summary>Cost and interval beside each site link.</summary>
    public bool LinkLabels { get; init; } = true;
    /// <summary>Shortcut, external, forest and realm trusts on the domains slide (parent-child and tree-root trusts are always drawn).</summary>
    public bool OtherTrusts { get; init; } = true;
    /// <summary>Leave out sites with no DCs. Off by default: an empty site is still part of the topology.</summary>
    public bool HideEmptySites { get; init; }
    /// <summary>
    /// ADTD.Net's "Aggregate empty sites": on the sites overview, two or more sites with no DCs that hang off the same
    /// site (or multi-site link) and nothing else are drawn as one "Aggregate Sites" box. Their own slides are kept.
    /// </summary>
    public bool ConsolidateEmptySites { get; init; }
    public bool Legend { get; init; } = true;
    /// <summary>A section header slide ahead of the forest, domains and trusts.</summary>
    public bool ForestSectionHeader { get; init; } = true;
    /// <summary>A section header slide ahead of the sites.</summary>
    public bool SitesSectionHeader { get; init; } = true;
    /// <summary>More DCs than this on one slide go onto "(cont.)" slides.</summary>
    public int MaxDcsPerSlide { get; init; } = 40;
    /// <summary>No text is set smaller than this.</summary>
    public double MinFontPt { get; init; } = 8;
}

/// <summary>
/// Lays out a forest as slides in two sections, each opened by a section header if wanted: "Forest, domains and trusts"
/// (a summary, the domains and trusts, a slide per domain), then "Sites" (the sites and site links, a slide per site). Everything is fitted to the layout's content area; text is never smaller than
/// <see cref="DeckOptions.MinFontPt"/>, and what doesn't fit at that size goes onto further slides.
/// </summary>
public static class DeckPlanner
{
    public const string ForestSection = "Forest, domains and trusts";
    public const string SitesSection = "Sites";

    public static DeckPlan Plan(ForestSnapshot forest, SlideFrame frame, ThemeInfo theme, ITextMeasurer measurer, double iconAspect, DeckOptions? options = null)
    {
        var c = new Ctx(forest, frame, theme, measurer, iconAspect, options ?? new DeckOptions());
        // Two sections: the forest, its domains and trusts; then the sites.
        var forestSlides = new List<SlidePlan>();
        if (c.O.Summary) forestSlides.Add(Summary.Plan(c));
        if (c.O.DomainsOverview && forest.Domains.Count > 0) forestSlides.AddRange(DomainsOverview.Plan(c));
        if (c.O.DomainDetails) foreach (var d in c.Domains) forestSlides.AddRange(DetailSlides.Domain(c, d));
        var siteSlides = new List<SlidePlan>();
        if (c.O.SitesOverview && c.Sites.Count > 0) siteSlides.AddRange(SitesOverview.Plan(c));
        if (c.O.SiteDetails) foreach (var s in c.Sites) siteSlides.AddRange(DetailSlides.Site(c, s));
        // A section header only separates the two sections: a deck of one section has none.
        var both = forestSlides.Count > 0 && siteSlides.Count > 0;
        if (c.O.ForestSectionHeader && both)
        {
            forestSlides.Insert(0, new SlidePlan(ForestSection, [])
            {
                Kind = SlideKind.SectionHeader,
                Subtitle = $"{forest.ForestName} · {Ctx.Count(forest.Domains.Count, "domain", "domains")} · {Ctx.Count(TrustCount(forest), "trust", "trusts")}",
            });
        }
        if (c.O.SitesSectionHeader && both)
            siteSlides.Insert(0, new SlidePlan(SitesSection, [])
            {
                Kind = SlideKind.SectionHeader,
                Subtitle = $"{Ctx.Count(c.Sites.Count, "site", "sites")} · {Ctx.Count(forest.SiteLinks.Count, "site link", "site links")}",
            });
        var slides = forestSlides.Select(s => s with { Section = DeckPlanner.ForestSection })
                                 .Concat(siteSlides.Select(s => s with { Section = DeckPlanner.SitesSection })).ToList();
        var warnings = c.Colors.Warnings.Concat(measurer.Warnings).Concat(c.Warnings).Distinct().ToList();
        return new DeckPlan(slides) { Warnings = warnings };
    }

    /// <summary>Trust relationships, each counted once: a trust inside the forest is recorded on both of its domains.</summary>
    static int TrustCount(ForestSnapshot f) =>
        f.Domains.SelectMany(d => d.Trusts.Select(t => string.Compare(d.DnsName, t.Partner, StringComparison.OrdinalIgnoreCase) < 0
                                                        ? (d.DnsName.ToLowerInvariant(), t.Partner.ToLowerInvariant())
                                                        : (t.Partner.ToLowerInvariant(), d.DnsName.ToLowerInvariant())))
                 .Distinct().Count();
}

/// <summary>Everything the planners share for one deck.</summary>
internal sealed class Ctx
{
    public ForestSnapshot F { get; }
    public SlideFrame Frame { get; }
    public ThemeInfo Theme { get; }
    public ITextMeasurer M { get; }
    public double IconAspect { get; }
    public DeckOptions O { get; }
    public ColorAssigner Colors { get; }
    public List<string> Warnings { get; } = [];

    /// <summary>Domains in tree order: the forest root, its children depth first, then the other trees.</summary>
    public IReadOnlyList<Domain> Domains { get; }
    public IReadOnlyList<Site> Sites { get; }
    readonly Dictionary<string, DomainController> _dcs;
    readonly Dictionary<string, int> _domainIndex;

    public Ctx(ForestSnapshot f, SlideFrame frame, ThemeInfo theme, ITextMeasurer m, double iconAspect, DeckOptions o)
    {
        F = f; Frame = frame; Theme = theme; M = m; IconAspect = iconAspect > 0 ? iconAspect : 1; O = o;
        Domains = TreeOrder(f);
        _domainIndex = Domains.Select((d, i) => (d.DnsName, i)).ToDictionary(x => x.DnsName, x => x.i, StringComparer.OrdinalIgnoreCase);
        Colors = new ColorAssigner(Domains.Select(d => d.DnsName), theme);
        _dcs = f.DomainControllers.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Sites = o.HideEmptySites ? f.Sites.Where(s => DcsIn(s.Name).Count > 0).ToList() : f.Sites;
    }

    public Rect Content => Frame.Content;
    public double Min => O.MinFontPt;
    public static double Pt(double points) => points * Emu.PerPoint;

    /// <summary>The sizes to try, largest first, never below the minimum (which is always tried last).</summary>
    public IReadOnlyList<double> Ladder(params double[] sizes) =>
        sizes.Where(s => s > Min).OrderByDescending(s => s).Append(Min).Distinct().ToList();

    public DomainController? Dc(string? key) => key != null && _dcs.TryGetValue(key, out var d) ? d : null;
    public string DcName(string? key) => key == null ? "-" : Dc(key)?.Name ?? FirstRdn(key);
    public int DomainIndex(string? dns) => dns != null && _domainIndex.TryGetValue(dns, out var i) ? i : int.MaxValue;

    public IReadOnlyList<DomainController> DcsIn(string site) =>
        F.DomainControllers.Where(d => string.Equals(d.Site, site, StringComparison.OrdinalIgnoreCase))
         .OrderBy(d => DomainIndex(d.Domain)).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<DomainController> DcsOf(string domain) =>
        F.DomainControllers.Where(d => string.Equals(d.Domain, domain, StringComparison.OrdinalIgnoreCase))
         .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<Subnet> SubnetsIn(string site) =>
        F.Subnets.Where(s => string.Equals(s.Site, site, StringComparison.OrdinalIgnoreCase)).OrderBy(s => SubnetOrder(s.Prefix)).ThenBy(s => s.Prefix).ToList();

    public static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    public static string LevelShort(int level) => FunctionalLevels.Name(level).Replace("Windows Server", "WS").Replace("Windows", "Win");

    static (uint, int) SubnetOrder(string prefix)
    {
        var parts = prefix.Split('/');
        if (IPAddress.TryParse(parts[0], out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return (((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3], parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 32);
        }
        return (uint.MaxValue, 0);
    }

    static string FirstRdn(string dn)
    {
        // "CN=NTDS Settings,CN=DC01,..." names the DC in its second RDN.
        var parts = dn.Split(',');
        var rdn = parts.Length > 1 && parts[0].StartsWith("CN=NTDS Settings", StringComparison.OrdinalIgnoreCase) ? parts[1] : parts[0];
        var eq = rdn.IndexOf('=');
        return eq >= 0 ? rdn[(eq + 1)..] : rdn;
    }

    static List<Domain> TreeOrder(ForestSnapshot f)
    {
        var names = f.Domains.Select(d => d.DnsName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var children = f.Domains.Where(d => d.ParentDns != null && names.Contains(d.ParentDns))
                                .GroupBy(d => d.ParentDns!, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.OrderBy(d => d.DnsName, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var roots = f.Domains.Where(d => d.ParentDns == null || !names.Contains(d.ParentDns))
                             .OrderBy(d => string.Equals(d.DnsName, f.ForestName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                             .ThenBy(d => d.DnsName, StringComparer.OrdinalIgnoreCase);
        var result = new List<Domain>();
        void Walk(Domain d)
        {
            result.Add(d);
            foreach (var c in children.GetValueOrDefault(d.DnsName) ?? []) Walk(c);
        }
        foreach (var r in roots) Walk(r);
        return result;
    }
}

/// <summary>A legend swatch: a filled chip, a line sample, or an outlined shape.</summary>
internal sealed record LegendEntry(string Label, Geometry Shape, ColorRef? Fill, Stroke? Line, bool IsLine = false);

/// <summary>Pieces several slide types use: the facts line under the title, legends and tables.</summary>
internal static class Parts
{
    public const string Separator = "   ·   ";

    /// <summary>
    /// One or two lines of facts across the top of <paramref name="area"/>, at the largest size that fits, with an
    /// optional colour chip in front. Returns the items and the height used (including the gap below).
    /// </summary>
    public static (List<PlanItem> Items, double Height) Facts(Ctx c, string name, IReadOnlyList<string> segments, Rect area, ColorRef? chip = null)
    {
        if (segments.Count == 0) return ([], 0);
        var items = new List<PlanItem>();
        foreach (var f in c.Ladder(12, 11, 10, 9))
        {
            var lineH = c.M.LineHeight(f);
            var chipW = chip != null ? lineH * 0.75 + Ctx.Pt(f * 0.6) : 0;
            var w = area.W - chipW;
            var lines = c.M.Wrap(segments, Separator, w, f, 2);
            if (lines == null && f > c.Min) continue;
            lines ??= ForceFit(c, segments, w, f);
            var h = lineH * lines.Count;
            if (chip != null)
            {
                var s = lineH * 0.75;
                items.Add(new BoxItem($"{name} colour", new Rect(area.X, area.Y + (lineH - s) / 2, s, s), Geometry.RoundRect) { Fill = chip, CornerRatio = 0.2 });
            }
            items.Add(new TextItem(name, new Rect(area.X + chipW, area.Y, w, h),
                PlanText.Lines(lines, f, new ThemeColor(SchemeSlot.Tx1)) with { Anchor = VAnchor.Top, Inset = 0, Wrap = false }));
            return (items, h + Ctx.Pt(f * 0.9));
        }
        return ([], 0);
    }

    static List<string> ForceFit(Ctx c, IReadOnlyList<string> segments, double w, double f)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var s in segments)
        {
            var candidate = current.Length == 0 ? s : current + Separator + s;
            if (current.Length > 0 && c.M.Width(candidate, f) > w) { lines.Add(current); current = s; }
            else current = candidate;
        }
        if (current.Length > 0) lines.Add(current);
        if (lines.Count > 2) lines = [lines[0], string.Join(Separator, lines.Skip(1))];
        return lines.Select(l => c.M.Truncate(l, w, f)).ToList();
    }

    /// <summary>The height a legend row takes at the bottom of an area (0 when there's nothing to show).</summary>
    public static double LegendHeight(Ctx c, int entries) => entries == 0 || !c.O.Legend ? 0 : c.M.LineHeight(LegendFont(c)) * 1.15 + Ctx.Pt(6);

    static double LegendFont(Ctx c) => Math.Max(c.Min, 9);

    /// <summary>
    /// A row of swatches and labels along the bottom of <paramref name="area"/>. Entries that don't fit are summarised
    /// as "+n more".
    /// </summary>
    public static List<PlanItem> Legend(Ctx c, IReadOnlyList<LegendEntry> entries, Rect area)
    {
        if (entries.Count == 0 || !c.O.Legend) return [];
        var f = LegendFont(c);
        var lineH = c.M.LineHeight(f);
        var rowH = lineH * 1.15;
        var y = area.Bottom - rowH;
        var x = area.X;
        var sw = lineH * 1.6;
        var gap = Ctx.Pt(f * 0.5);
        var items = new List<PlanItem>();
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var tw = c.M.Width(e.Label, f);
            var need = sw + gap + tw + gap * 3;
            var moreW = c.M.Width($"+{entries.Count - i} more", f);
            if (x + need > area.Right - (i < entries.Count - 1 ? moreW : 0))
            {
                var more = $"+{entries.Count - i} more";
                if (x + moreW <= area.Right)
                    items.Add(new TextItem("Legend more", new Rect(x, y, moreW, rowH), PlanText.Simple(more, f, new ThemeColor(SchemeSlot.Tx1)) with { Inset = 0, Wrap = false }));
                break;
            }
            Rect swatch = e.IsLine ? new Rect(x, y + rowH / 2, sw, 0) : new Rect(x + sw * 0.15, y + rowH * 0.15, sw * 0.7, rowH * 0.7);
            items.Add(new BoxItem($"Legend {e.Label} swatch", swatch, e.Shape) { Fill = e.Fill, Line = e.Line, CornerRatio = 0.2 });
            items.Add(new TextItem($"Legend {e.Label}", new Rect(x + sw + gap, y, tw, rowH), PlanText.Simple(e.Label, f, new ThemeColor(SchemeSlot.Tx1)) with { Inset = 0, Wrap = false }));
            x += need;
        }
        return items;
    }

    public static double CellInset(double f) => Ctx.Pt(Math.Max(2, f * 0.4));
    public static double RowHeight(Ctx c, double f) => c.M.LineHeight(f) + Ctx.Pt(5);

    /// <summary>How many body rows (under a header) fit in <paramref name="height"/> at the smallest size.</summary>
    public static int RowCapacity(Ctx c, double height) => Math.Max(1, (int)Math.Floor(height / RowHeight(c, c.Min)) - 1);

    /// <summary>
    /// A table at the top-left of <paramref name="area"/>, at the largest of <paramref name="sizes"/> where its rows fit
    /// the height and its text fits the width (text is shortened with an ellipsis at the smallest size). Columns are
    /// sized to their text; the table is widened by <paramref name="growth"/> but kept within the area, and at least
    /// <paramref name="minWidth"/> of it.
    /// </summary>
    public static TableItem Table(Ctx c, string name, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows, Rect area,
                                  IReadOnlyList<double> sizes, double growth = 1.2, double minWidth = 0)
    {
        var cols = Math.Max(header.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
        foreach (var f in sizes)
        {
            var rowH = RowHeight(c, f);
            var h = rowH * (rows.Count + (header.Count > 0 ? 1 : 0));
            var last = f <= sizes[^1];
            if (h > area.H + 1 && !last) continue;
            var ins = CellInset(f);
            var need = Enumerable.Range(0, cols).Select(j =>
                Math.Max(j < header.Count ? c.M.Width(header[j], f, true) : 0,
                         rows.Select(r => j < r.Count ? c.M.Width(r[j], f) : 0).DefaultIfEmpty(0).Max()) + 2 * ins + Ctx.Pt(2)).ToArray();
            if (need.Sum() > area.W && !last) continue;
            var widths = Share(need, area.W, growth, minWidth);
            string Cell(string s, int j, bool bold) => c.M.Truncate(s, widths[j] - 2 * ins - Ctx.Pt(1), f, bold);
            var hdr = header.Select((s, j) => Cell(s, j, true)).ToList();
            var body = rows.Select(r => (IReadOnlyList<string>)r.Select((s, j) => Cell(s, j, false)).ToList()).ToList();
            return new TableItem(name, new Rect(area.X, area.Y, widths.Sum(), h), hdr, body, f) { ColumnWeights = widths, RowHeight = rowH };
        }
        throw new InvalidOperationException("No sizes to try.");
    }

    /// <summary>Column widths: what each needs, grown towards <paramref name="growth"/> × that, or shared out fairly when they need more than there is.</summary>
    static List<double> Share(double[] need, double width, double growth, double minWidth)
    {
        var total = need.Sum();
        if (total <= width)
        {
            var target = Math.Min(width, Math.Max(total * growth, Math.Min(width, minWidth)));
            return need.Select(n => Math.Floor(n * target / total)).ToList();
        }
        // Water-filling: narrow columns keep what they need; the rest share what's left.
        var result = new double[need.Length];
        var order = Enumerable.Range(0, need.Length).OrderBy(i => need[i]).ToList();
        var remaining = width;
        for (var k = 0; k < order.Count; k++)
        {
            var share = remaining / (order.Count - k);
            var i = order[k];
            result[i] = Math.Floor(Math.Min(need[i], share));
            remaining -= result[i];
        }
        return result.ToList();
    }
}
