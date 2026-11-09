using Adtd.Model;

namespace Adtd.Layout;

/// <summary>A slide per site and per domain.</summary>
internal static class DetailSlides
{
    // ---- site ------------------------------------------------------------------------------------

    /// <summary>
    /// A site: facts across the top, its DCs as a grid of cards, and its subnets as a table beside them. Too many DCs or
    /// subnets for one slide continue on "(cont.)" slides. A site without DCs says so, with its subnets below.
    /// </summary>
    public static IEnumerable<SlidePlan> Site(Ctx c, Site site)
    {
        var area = c.Content;
        var dcs = c.DcsIn(site.Name);
        var subnets = c.O.Subnets ? c.SubnetsIn(site.Name) : [];
        var cards = dcs.Select(d => Cards.For(c, d, string.Equals(d.Key, site.Istg, StringComparison.OrdinalIgnoreCase))).ToList();
        var title = $"Site: {site.Name}";

        var (facts, fh) = Parts.Facts(c, "Site facts", SiteFacts(c, site, dcs, subnets), area);
        var domains = dcs.Select(d => d.Domain).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var legend = domains.Count > 1
            ? domains.Select(d => new LegendEntry(d ?? "Domain unknown", Geometry.RoundRect, c.Colors.Of(d), null)).ToList()
            : [];
        var lh = Parts.LegendHeight(c, legend.Count);
        var body = new Rect(area.X, area.Y + fh, area.W, area.H - fh - lh);
        var gap = Emu.Inches(0.25);

        TextItem? note = null;
        double noteH = 0;
        if (dcs.Count == 0)
        {
            var f = Math.Max(c.Min, 12);
            var text = "No domain controllers in this site.";
            note = new TextItem("No DCs", new Rect(body.X, body.Y, c.M.Width(text, f), c.M.LineHeight(f)),
                PlanText.Simple(text, f, new ThemeColor(SchemeSlot.Tx1)) with { Anchor = VAnchor.Top, Inset = 0, Wrap = false });
            noteH = c.M.LineHeight(f) + Ctx.Pt(f * 0.8);
        }

        var (header, rows) = SubnetRows(subnets);
        double sideW = 0;
        if (cards.Count > 0 && subnets.Count > 0)
        {
            var probe = Parts.Table(c, "probe", header, rows, new Rect(0, 0, body.W * 0.36, double.MaxValue), [Math.Max(c.Min, 10)], growth: 1.1);
            sideW = Math.Clamp(probe.Bounds.W, body.W * 0.24, body.W * 0.36);
        }
        var cardsW = sideW > 0 ? body.W - sideW - gap : body.W;
        var cardChunks = cards.Count == 0 ? [] : Grid.Chunk(cards, Math.Min(c.O.MaxDcsPerSlide, Math.Max(1, Grid.Capacity(c, cards, cardsW, body.H))));
        var rowChunks = rows.Count == 0 ? [] : Grid.Chunk(rows, Parts.RowCapacity(c, body.H - noteH));
        var pages = Math.Max(1, Math.Max(cardChunks.Count, rowChunks.Count));
        if (pages > 1) c.Warnings.Add($"Site {site.Name} continues over {pages} slides.");

        for (var i = 0; i < pages; i++)
        {
            var items = new List<PlanItem>(facts);
            var ks = i < cardChunks.Count ? cardChunks[i] : [];
            var rs = i < rowChunks.Count ? rowChunks[i] : [];
            double gridRight = body.X;
            if (i == 0 && note != null) items.Add(note);
            if (ks.Count > 0)
            {
                var cardArea = rs.Count > 0 ? body with { W = cardsW } : body;
                var fit = GridOrFallback(c, ks, cardArea, title);
                items.AddRange(Grid.Place(c, ks, fit, cardArea.X, cardArea.Y));
                if (c.O.Replication) items.AddRange(ReplicationLinks(c, ks, fit, cardArea.X, cardArea.Y));
                items.AddRange(Parts.Legend(c, legend, area));
                gridRight = cardArea.X + fit.Width;
            }
            if (rs.Count > 0)
            {
                var top = i == 0 ? noteH : 0;
                // Beside the cards: close up to them when they don't fill their side.
                var tableArea = ks.Count > 0
                    ? new Rect(Math.Min(body.Right - sideW, gridRight + gap * 1.5), body.Y, sideW, body.H)
                    : new Rect(body.X, body.Y + top, body.W * 0.6, body.H - top);
                items.Add(Parts.Table(c, "Subnets", header, rs, tableArea, c.Ladder(12, 11, 10, 9),
                    growth: ks.Count > 0 ? 1 : 1.3, minWidth: ks.Count > 0 ? tableArea.W : 0));
            }
            yield return new SlidePlan(i == 0 ? title : title + " (cont.)", items);
        }
    }

    static (List<string> Header, List<IReadOnlyList<string>> Rows) SubnetRows(IReadOnlyList<Subnet> subnets)
    {
        bool loc = subnets.Any(s => !string.IsNullOrWhiteSpace(s.Location)), desc = subnets.Any(s => !string.IsNullOrWhiteSpace(s.Description));
        if (!loc && !desc) return (["Subnet"], subnets.Select(s => (IReadOnlyList<string>)[s.Prefix]).ToList());
        var second = loc && desc ? "Location / description" : loc ? "Location" : "Description";
        return (["Subnet", second], subnets.Select(s => (IReadOnlyList<string>)[s.Prefix,
            string.Join(" — ", new[] { s.Location, s.Description }.Where(x => !string.IsNullOrWhiteSpace(x)))]).ToList());
    }

    static List<string> SiteFacts(Ctx c, Site site, IReadOnlyList<DomainController> dcs, IReadOnlyList<Subnet> subnets)
    {
        var segs = new List<string>();
        if (!string.IsNullOrWhiteSpace(site.Location)) segs.Add($"Location: {site.Location}");
        if (!string.IsNullOrWhiteSpace(site.Description)) segs.Add(site.Description!);
        segs.Add(dcs.Count == 0 ? "No DCs" : Ctx.Count(dcs.Count, "DC", "DCs"));
        if (c.O.Subnets) segs.Add(subnets.Count == 0 ? "No subnets" : Ctx.Count(subnets.Count, "subnet", "subnets"));
        if (site.Istg != null) segs.Add($"ISTG: {c.DcName(site.Istg)}");
        if (site.UniversalGroupCaching) segs.Add("Universal group caching on");
        var links = c.F.SiteLinks.Where(l => l.Sites.Contains(site.Name, StringComparer.OrdinalIgnoreCase))
                                 .Select(l => $"{l.Name} ({l.Cost}, {l.ReplicationIntervalMinutes} min)").ToList();
        if (links.Count == 0) segs.Add("No site links");
        else { links[0] = "Site links: " + links[0]; segs.AddRange(links); }
        return segs;
    }

    static Grid.Fit GridOrFallback(Ctx c, IReadOnlyList<Card> cards, Rect area, string title)
    {
        if (Grid.Find(c, cards, area.W, area.H) is { } fit) return fit;
        c.Warnings.Add($"\"{title}\": the DC cards don't fit the content area even at {c.Min} pt.");
        var m = Cards.Measure(c, cards, c.Min);
        var g = Grid.GapFor(c.Min);
        var cols = Math.Max(1, (int)Math.Floor((area.W + g) / (m.W + g)));
        return new Grid.Fit(m, cols, (cards.Count + cols - 1) / cols, g);
    }

    /// <summary>Connectors for the replication connections between DCs on the slide, pointing the way changes flow.</summary>
    static IEnumerable<PlanItem> ReplicationLinks(Ctx c, IReadOnlyList<Card> cards, Grid.Fit fit, double x, double y)
    {
        var at = cards.Select((k, i) => (k.Key, R: new Rect(x + i % fit.Cols * (fit.M.W + fit.Gap), y + i / fit.Cols * (fit.M.H + fit.Gap), fit.M.W, fit.M.H)))
                      .ToDictionary(t => t.Key, t => t.R, StringComparer.OrdinalIgnoreCase);
        var pairs = new Dictionary<(string, string), (ArrowEnds Arrows, bool Enabled, string Name)>();
        foreach (var cn in c.F.Connections)
        {
            string from = "dc:" + cn.From, to = "dc:" + cn.To;
            if (!at.ContainsKey(from) || !at.ContainsKey(to) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
            if (pairs.TryGetValue((to, from), out var back)) { pairs[(to, from)] = back with { Arrows = ArrowEnds.Both, Enabled = back.Enabled || cn.Enabled }; continue; }
            pairs.TryAdd((from, to), (ArrowEnds.End, cn.Enabled, cn.Name));
        }
        foreach (var ((from, to), v) in pairs)
        {
            var (a, b) = Geo.Nearest(Geometry.RoundRect, at[from], Geometry.RoundRect, at[to]);
            yield return new LinkItem($"Replication {c.DcName(from[3..])}-{c.DcName(to[3..])}", from, to, [a, b],
                new Stroke(new ThemeColor(SchemeSlot.Tx2), 1, v.Enabled ? Dash.Solid : Dash.Dash)) { Arrows = v.Arrows };
        }
    }

    // ---- domain ----------------------------------------------------------------------------------

    sealed record SiteGroup(string Label, IReadOnlyList<Card> Cards);

    sealed record Packed(double FontPt, CardMetrics M, double Gap, List<(SiteGroup Group, Rect Frame, int Cols)> Boxes, double Pad, double HeadH);

    /// <summary>
    /// A domain: facts (with its colour) across the top, then its DCs as cards grouped by site. Sites that don't fit
    /// continue on "(cont.)" slides.
    /// </summary>
    public static IEnumerable<SlidePlan> Domain(Ctx c, Domain d)
    {
        var area = c.Content;
        var dcs = c.DcsOf(d.DnsName);
        var title = $"Domain: {d.DnsName}";
        var (facts, fh) = Parts.Facts(c, "Domain facts", DomainFacts(c, d, dcs), area, c.Colors.Of(d.DnsName));
        var body = new Rect(area.X, area.Y + fh, area.W, area.H - fh);

        if (dcs.Count == 0)
        {
            var f = Math.Max(c.Min, 12);
            var text = d.Reachable ? "No domain controllers found for this domain." : "No domain controllers could be read for this domain.";
            yield return new SlidePlan(title, [.. facts, new TextItem("No DCs", new Rect(body.X, body.Y, c.M.Width(text, f), c.M.LineHeight(f)),
                PlanText.Simple(text, f, new ThemeColor(SchemeSlot.Tx1)) with { Anchor = VAnchor.Top, Inset = 0, Wrap = false })]);
            yield break;
        }

        var groups = dcs.GroupBy(x => x.Site, StringComparer.OrdinalIgnoreCase)
                        .Select(g => new SiteGroup(g.Key, g.Select(x => Cards.For(c, x, false)).ToList()))
                        // Sites holding operations master roles first, so they're on the first slide; then the biggest.
                        .OrderByDescending(g => g.Cards.Any(k => k.Badges.Any(Cards.IsFsmo)))
                        .ThenByDescending(g => g.Cards.Count).ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var pages = Paginate(c, groups, body);
        if (pages.Count > 1) c.Warnings.Add($"Domain {d.DnsName} continues over {pages.Count} slides.");
        for (var i = 0; i < pages.Count; i++)
        {
            var items = new List<PlanItem>(facts);
            var pack = Pack(c, pages[i], body, 14) ?? Pack(c, pages[i], body with { H = double.MaxValue }, c.Min)!;
            if (pack.Boxes.Max(b => b.Frame.Bottom) > body.Bottom + 1) c.Warnings.Add($"\"{title}\": the DC cards don't fit the content area even at {c.Min} pt.");
            foreach (var (g, frame, cols) in pack.Boxes)
            {
                var label = c.M.Truncate($"{g.Label} · {Ctx.Count(g.Cards.Count, "DC", "DCs")}", frame.W - 2 * pack.Pad, pack.FontPt, true);
                var children = new List<PlanItem>
                {
                    new BoxItem($"Site {g.Label} frame", frame, Geometry.RoundRect) { CornerRatio = 0.04, Line = new Stroke(new ThemeColor(SchemeSlot.Tx1, Alpha: 45000), 0.75) },
                    new TextItem($"Site {g.Label} name", new Rect(frame.X + pack.Pad, frame.Y + pack.Pad, frame.W - 2 * pack.Pad, c.M.LineHeight(pack.FontPt)),
                        PlanText.Simple(label, pack.FontPt, new ThemeColor(SchemeSlot.Tx1), bold: true) with { Anchor = VAnchor.Top, Inset = 0, Wrap = false }),
                };
                var fit = new Grid.Fit(pack.M, cols, (g.Cards.Count + cols - 1) / cols, pack.Gap);
                children.AddRange(Grid.Place(c, g.Cards, fit, frame.X + pack.Pad, frame.Y + pack.Pad + pack.HeadH));
                items.Add(new GroupItem($"Site {g.Label}", children));
            }
            yield return new SlidePlan(i == 0 ? title : title + " (cont.)", items);
        }
    }

    static List<string> DomainFacts(Ctx c, Domain d, IReadOnlyList<DomainController> dcs)
    {
        var segs = new List<string>();
        if (d.NetBiosName.Length > 0) segs.Add(d.NetBiosName);
        segs.Add($"Functional level: {FunctionalLevels.Name(d.FunctionalLevel)}");
        if (!d.Reachable) segs.Add("Not reachable when collected: roles, trusts and DC details may be missing");
        segs.Add($"PDC: {c.DcName(d.PdcMaster)}");
        segs.Add($"RID: {c.DcName(d.RidMaster)}");
        segs.Add($"Infrastructure: {c.DcName(d.InfrastructureMaster)}");
        var sites = dcs.Select(x => x.Site).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        segs.Add(dcs.Count == 0 ? "No DCs" : $"{Ctx.Count(dcs.Count, "DC", "DCs")} in {Ctx.Count(sites, "site", "sites")}");
        if (d.ParentDns != null) segs.Add($"Parent: {d.ParentDns}");
        var children = c.Domains.Where(x => string.Equals(x.ParentDns, d.DnsName, StringComparison.OrdinalIgnoreCase)).Select(x => x.DnsName).ToList();
        if (children.Count > 0) segs.Add($"Child domains: {string.Join(", ", children)}");
        foreach (var t in d.Trusts.Where(t => t.Kind is not (TrustKind.ParentChild or TrustKind.TreeRoot)))
        {
            var arrow = t.Direction switch { TrustDirection.Bidirectional => "↔", TrustDirection.Outbound => "→", TrustDirection.Inbound => "←", _ => "(disabled)" };
            segs.Add($"{t.Kind} trust {arrow} {t.Partner}");
        }
        return segs;
    }

    /// <summary>Site groups split into slides: all on one if they fit at the minimum size, else as many per slide as fit.</summary>
    static List<List<SiteGroup>> Paginate(Ctx c, List<SiteGroup> groups, Rect body)
    {
        int Count(IEnumerable<SiteGroup> gs) => gs.Sum(g => g.Cards.Count);
        bool Fits(List<SiteGroup> gs) => Count(gs) <= c.O.MaxDcsPerSlide && Pack(c, gs, body, c.Min) != null;
        if (Fits(groups)) return [groups];

        var pages = new List<List<SiteGroup>>();
        var cur = new List<SiteGroup>();
        foreach (var g in groups)
        {
            if (Fits([.. cur, g])) { cur.Add(g); continue; }
            if (cur.Count > 0) { pages.Add(cur); cur = []; }
            if (Fits([g])) { cur.Add(g); continue; }
            // One site with more DCs than a slide holds: split it.
            var m = Cards.Measure(c, g.Cards, c.Min);
            var gap = Grid.GapFor(c.Min);
            var pad = Ctx.Pt(0.6 * c.Min);
            var head = c.M.LineHeight(c.Min) + Ctx.Pt(0.4 * c.Min);
            var cols = Math.Max(1, (int)Math.Floor((body.W - 2 * pad + gap) / (m.W + gap)));
            var rows = Math.Max(1, (int)Math.Floor((body.H - 2 * pad - head + gap) / (m.H + gap)));
            var chunks = Grid.Chunk(g.Cards, Math.Min(c.O.MaxDcsPerSlide, cols * rows));
            for (var i = 0; i < chunks.Count; i++)
                pages.Add([new SiteGroup(i == 0 ? g.Label : g.Label + " (cont.)", chunks[i])]);
        }
        if (cur.Count > 0) pages.Add(cur);
        return pages;
    }

    /// <summary>
    /// Site containers packed in rows from the left at the largest card font up to <paramref name="maxFont"/>
    /// where they fit; each container's columns are capped so wide sites wrap. Null if they don't fit at the minimum.
    /// </summary>
    static Packed? Pack(Ctx c, List<SiteGroup> groups, Rect area, double maxFont)
    {
        var all = groups.SelectMany(g => g.Cards).ToList();
        var maxK = groups.Max(g => g.Cards.Count);
        for (var f = maxFont; f >= c.Min - 1e-9; f -= 0.5)
        {
            var m = Cards.Measure(c, all, f);
            var g = Grid.GapFor(f);
            var pad = Ctx.Pt(0.6 * f);
            var head = c.M.LineHeight(f) + Ctx.Pt(0.4 * f);
            var between = g * 1.3;
            for (var cap = maxK; cap >= 1; cap--)
            {
                var sizes = groups.Select(gr =>
                {
                    var cols = Math.Min(gr.Cards.Count, cap);
                    var rows = (gr.Cards.Count + cols - 1) / cols;
                    return (Cols: cols, W: 2 * pad + cols * m.W + (cols - 1) * g, H: 2 * pad + head + rows * m.H + (rows - 1) * g);
                }).ToList();
                if (sizes.Any(s => s.W > area.W)) continue;

                // Shelf packing.
                var shelves = new List<List<int>> { new() };
                double x = 0;
                for (var i = 0; i < sizes.Count; i++)
                {
                    if (shelves[^1].Count > 0 && x + between + sizes[i].W > area.W) { shelves.Add([]); x = 0; }
                    x += (shelves[^1].Count > 0 ? between : 0) + sizes[i].W;
                    shelves[^1].Add(i);
                }
                var total = shelves.Sum(s => s.Max(i => sizes[i].H)) + between * (shelves.Count - 1);
                if (total > area.H) continue;

                var boxes = new List<(SiteGroup, Rect, int)>();
                var y = area.Y;
                foreach (var shelf in shelves)
                {
                    var sx = area.X;
                    foreach (var i in shelf)
                    {
                        boxes.Add((groups[i], new Rect(sx, y, sizes[i].W, sizes[i].H), sizes[i].Cols));
                        sx += sizes[i].W + between;
                    }
                    y += shelf.Max(i => sizes[i].H) + between;
                }
                return new Packed(f, m, g, boxes, pad, head);
            }
        }
        return null;
    }
}

/// <summary>Geometry helpers.</summary>
internal static class Geo
{
    /// <summary>The pair of connection sites, one on each shape, that are closest together.</summary>
    public static (Point A, Point B) Nearest(Geometry ga, Rect a, Geometry gb, Rect b)
    {
        var best = (A: default(Point), B: default(Point));
        var bestD = double.MaxValue;
        foreach (var p in Sites.For(ga, a))
            foreach (var q in Sites.For(gb, b))
            {
                var d = (p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y);
                if (d < bestD) { bestD = d; best = (p, q); }
            }
        return best;
    }
}
