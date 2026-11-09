using Adtd.Model;

namespace Adtd.Layout;

/// <summary>
/// The domains and trusts: a tidy tree per domain tree, forest root first, parent-child trusts as elbow connectors and
/// tree-root trusts along a bus above the roots. Shortcut trusts, and trusts with domains outside the forest (in a
/// column on the right), are dashed with arrows pointing to the trusted domain.
/// </summary>
internal static class DomainsOverview
{
    /// <summary>Domains outside the forest: drawn like the sites.</summary>
    static readonly ThemeColor OutsideFill = new ThemeColor(SchemeSlot.Tx2).Lighter(0.82);

    sealed record External(string Name, string Kind, List<(string Domain, ArrowEnds Arrows)> Links);

    public static IEnumerable<SlidePlan> Plan(Ctx c)
    {
        var names = c.Domains.Select(d => d.DnsName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = c.Domains.Where(d => d.ParentDns == null || !names.Contains(d.ParentDns)).ToList();
        var shortcuts = new List<(string A, string B, ArrowEnds Arrows)>();
        var externals = new Dictionary<string, External>(StringComparer.OrdinalIgnoreCase);
        if (c.O.OtherTrusts)
            foreach (var d in c.Domains)
                foreach (var t in d.Trusts.Where(t => t.Kind is not (TrustKind.ParentChild or TrustKind.TreeRoot)))
                {
                    var arrows = Arrows(t.Direction);
                    if (names.Contains(t.Partner))
                    {
                        if (shortcuts.Any(s => Same(s.A, t.Partner) && Same(s.B, d.DnsName) || Same(s.A, d.DnsName) && Same(s.B, t.Partner))) continue;
                        shortcuts.Add((d.DnsName, t.Partner, arrows));
                    }
                    else
                    {
                        if (!externals.TryGetValue(t.Partner, out var e))
                            externals[t.Partner] = e = new External(t.Partner, KindText(t), []);
                        e.Links.Add((d.DnsName, arrows));
                    }
                }

        foreach (var f in c.Ladder(14, 12, 10, 9))
            if (Layout(c, f, roots, shortcuts, externals.Values.ToList(), "Domains and trusts", force: false) is { } plan)
                return [plan];

        // Too many domains for one slide: a slide per tree.
        if (roots.Count == 1)
            return [Layout(c, c.Min, roots, shortcuts, externals.Values.ToList(), "Domains and trusts", force: true)!];
        c.Warnings.Add($"The domains slide is split into {roots.Count} slides, one per domain tree.");
        var slides = new List<SlidePlan>();
        for (var i = 0; i < roots.Count; i++)
        {
            var inTree = Subtree(c, roots[i]).Select(d => d.DnsName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sc = shortcuts.Where(s => inTree.Contains(s.A) && inTree.Contains(s.B)).ToList();
            var ex = externals.Values.Select(e => e with { Links = e.Links.Where(l => inTree.Contains(l.Domain)).ToList() }).Where(e => e.Links.Count > 0).ToList();
            var title = $"Domains and trusts ({i + 1}/{roots.Count})";
            slides.Add(c.Ladder(14, 12, 10, 9).Select(f => Layout(c, f, [roots[i]], sc, ex, title, false)).FirstOrDefault(p => p != null)
                       ?? Layout(c, c.Min, [roots[i]], sc, ex, title, true)!);
        }
        return slides;
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static ArrowEnds Arrows(TrustDirection d) => d switch
    {
        TrustDirection.Outbound => ArrowEnds.End,
        TrustDirection.Inbound => ArrowEnds.Start,
        TrustDirection.Bidirectional => ArrowEnds.Both,
        _ => ArrowEnds.None,
    };

    static string KindText(Trust t) => (t.Kind switch
    {
        TrustKind.Forest => "Forest trust",
        TrustKind.External => "External trust",
        TrustKind.Realm => "Kerberos realm",
        _ => "Trust",
    }) + (string.IsNullOrEmpty(t.FlatName) ? "" : $" · {t.FlatName}");

    static IEnumerable<Domain> Subtree(Ctx c, Domain root)
    {
        yield return root;
        foreach (var ch in c.Domains.Where(d => Same(d.ParentDns ?? "", root.DnsName)))
            foreach (var x in Subtree(c, ch)) yield return x;
    }

    static SlidePlan? Layout(Ctx c, double f, List<Domain> roots, List<(string A, string B, ArrowEnds Arrows)> shortcuts, List<External> externals,
                             string title, bool force)
    {
        var area = c.Content;
        var m = c.M;
        var lineH = m.LineHeight(f);
        var ins = Ctx.Pt(0.5 * f);
        var domains = roots.SelectMany(r => Subtree(c, r)).ToList();
        var inSlide = domains.Select(d => d.DnsName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        shortcuts = shortcuts.Where(s => inSlide.Contains(s.A) && inSlide.Contains(s.B)).ToList();

        List<string> Lines(Domain d) =>
        [
            d.DnsName,
            $"{(d.NetBiosName.Length > 0 ? d.NetBiosName + " · " : "")}{Ctx.Count(c.DcsOf(d.DnsName).Count, "DC", "DCs")}",
            d.Reachable ? $"Functional level {Ctx.LevelShort(d.FunctionalLevel)}" : $"Not reachable · {Ctx.LevelShort(d.FunctionalLevel)}",
        ];
        var lines = domains.ToDictionary(d => d.DnsName, Lines, StringComparer.OrdinalIgnoreCase);
        double TextW(List<string> ls) => ls.Select((l, i) => m.Width(l, f, i == 0)).Max();
        var w = lines.Values.Max(TextW) + 2 * ins;
        var h = 3 * lineH + 2 * ins;
        var exLines = externals.ToDictionary(e => e.Name, e => new List<string> { e.Name, e.Kind }, StringComparer.OrdinalIgnoreCase);
        var we = exLines.Count == 0 ? 0 : exLines.Values.Max(TextW) + 2 * ins;
        var he = 2 * lineH + 2 * ins;

        // Tight gaps to test the fit; they're spread out again below when there's room.
        var hgap = Math.Max(Ctx.Pt(8), 0.2 * w);
        var vgap = 0.7 * h;
        var treeGap = 0.35 * w;
        var bus = roots.Count > 1 ? 0.5 * h : 0;

        // Tidy tree: leaves take consecutive slots, parents sit over the middle of their children.
        var cx = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        double cursor = 0;
        double Place(Domain d, int level)
        {
            depth[d.DnsName] = level;
            var kids = c.Domains.Where(x => Same(x.ParentDns ?? "", d.DnsName)).ToList();
            double x;
            if (kids.Count == 0) { x = cursor + w / 2; cursor += w + hgap; }
            else { var xs = kids.Select(k => Place(k, level + 1)).ToList(); x = (xs[0] + xs[^1]) / 2; }
            return cx[d.DnsName] = x;
        }
        foreach (var r in roots) { Place(r, 0); cursor += treeGap; }
        var maxDepth = depth.Values.Max();
        var treesW = cursor - treeGap - hgap;
        var treesH = bus + (maxDepth + 1) * h + maxDepth * vgap;

        // Outside the forest: a column on the right, ordered by the height of the domains they trust.
        var exGap = 0.5 * he;
        var colGap = externals.Count > 0 ? Math.Max(w * 0.4, Ctx.Pt(24)) : 0;
        var exOrder = externals.OrderBy(e => e.Links.Min(l => depth.GetValueOrDefault(l.Domain))).ThenBy(e => e.Links.Min(l => cx.GetValueOrDefault(l.Domain)))
                               .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var exH = externals.Count * he + Math.Max(0, externals.Count - 1) * exGap;
        var totalW = treesW + (externals.Count > 0 ? colGap + we : 0);
        var totalH = Math.Max(treesH, exH);

        var legend = new List<LegendEntry>();
        if (shortcuts.Count > 0 || externals.Count > 0)
        {
            var tx = new ThemeColor(SchemeSlot.Tx1);
            legend.Add(new LegendEntry("Parent-child and tree-root trusts", Geometry.Rect, null, new Stroke(tx, 1.5), IsLine: true));
            legend.Add(new LegendEntry("Other trusts (arrow points to the trusted domain)", Geometry.Rect, null, new Stroke(new ThemeColor(SchemeSlot.Tx2), 1.25, Dash.Dash), IsLine: true));
            if (externals.Count > 0)
                legend.Add(new LegendEntry("Outside the forest", Geometry.RoundRect, OutsideFill, new Stroke(new ThemeColor(SchemeSlot.Tx2), 1)));
        }
        var lh = Parts.LegendHeight(c, legend.Count);
        var availH = area.H - lh;
        if (!force && (totalW > area.W || totalH > availH)) return null;

        // Spread into the space: up to 2.6× across and 2.2× down, keeping the stretch within 2:1.
        var spanX = totalW - w / 2 - (externals.Count > 0 ? we / 2 : w / 2);
        var spanY = totalH - h;
        var kx = spanX > 0 ? Math.Clamp((area.W - (totalW - spanX)) / spanX, 1, 2.6) : 1;
        var ky = spanY > 0 ? Math.Clamp((availH - (totalH - spanY)) / spanY, 1, 2.2) : 1;
        kx = Math.Min(kx, 2 * ky);
        ky = Math.Min(ky, 2 * kx);
        var outW = (totalW - spanX) + spanX * kx;
        var outH = (totalH - spanY) + spanY * ky;
        var ox = area.X + Math.Max(0, (area.W - outW) / 2);
        var oy = area.Y + Math.Max(0, (availH - outH) / 2);
        double X(double natural) => ox + w / 2 + (natural - w / 2) * kx;   // natural centre x → slide centre x
        double Y(double natural, double nodeH) => oy + nodeH / 2 + (natural - nodeH / 2) * ky;

        var items = new List<PlanItem>();
        var boxes = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in domains)
        {
            var centreY = Y(bus + depth[d.DnsName] * (h + vgap) + h / 2, h);
            var r = new Rect(X(cx[d.DnsName]) - w / 2, centreY - h / 2, w, h);
            boxes[d.DnsName] = r;
            var colour = c.Colors.Of(d.DnsName);
            // Every domain is the same solid rounded rectangle, like the sites; one that couldn't be read is paler.
            var fill = d.Reachable ? colour : colour.Lighter(0.6);
            items.Add(new BoxItem($"Domain {d.DnsName}", r, Geometry.RoundRect)
            {
                Key = "dom:" + d.DnsName, Fill = fill, Line = new Stroke(colour.Darker(0.25), 1), CornerRatio = 0.2,
                Text = PlanText.Lines(lines[d.DnsName], f, c.Theme.TextOn(fill), boldFirst: true, HAlign.Center) with { Inset = ins, Wrap = false },
            });
        }
        var exBoxes = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        var exX = ox + outW - we;
        var step = (he + exGap) * ky;
        if (exOrder.Count > 1) step = Math.Min(step, (outH - he) / (exOrder.Count - 1));
        for (var i = 0; i < exOrder.Count; i++)
        {
            var e = exOrder[i];
            var r = new Rect(exX, oy + i * step, we, he);
            exBoxes[e.Name] = r;
            items.Add(new BoxItem($"Outside {e.Name}", r, Geometry.RoundRect)
            {
                Key = "ext:" + e.Name, Fill = OutsideFill, Line = new Stroke(new ThemeColor(SchemeSlot.Tx2), 1), CornerRatio = 0.2,
                Text = PlanText.Lines(exLines[e.Name], f, new ThemeColor(SchemeSlot.Tx1), boldFirst: true, HAlign.Center) with { Inset = ins, Wrap = false },
            });
        }

        var structural = new Stroke(new ThemeColor(SchemeSlot.Tx1), 1.5);
        var other = new Stroke(new ThemeColor(SchemeSlot.Tx2), 1.25, Dash.Dash);
        foreach (var d in domains.Where(d => d.ParentDns != null && boxes.ContainsKey(d.ParentDns)))
        {
            Rect p = boxes[d.ParentDns!], k = boxes[d.DnsName];
            var mid = (p.Bottom + k.Y) / 2;
            IReadOnlyList<Point> route = Math.Abs(p.CenterX - k.CenterX) < 1
                ? [new(p.CenterX, p.Bottom), new(k.CenterX, k.Y)]
                : [new(p.CenterX, p.Bottom), new(p.CenterX, mid), new(k.CenterX, mid), new(k.CenterX, k.Y)];
            items.Add(new LinkItem($"Trust {d.ParentDns}-{d.DnsName}", "dom:" + d.ParentDns, "dom:" + d.DnsName, route, structural));
        }
        if (roots.Count > 1)
        {
            // Every tree root trusts the forest root: out of its right side, up to a bus over the roots, down onto each.
            var a = boxes[roots[0].DnsName];
            var busY = oy + bus * ky * 0.4;
            var e = hgap * kx * 0.4;
            foreach (var r in roots.Skip(1))
            {
                var b = boxes[r.DnsName];
                items.Add(new LinkItem($"Trust {roots[0].DnsName}-{r.DnsName}", "dom:" + roots[0].DnsName, "dom:" + r.DnsName,
                    [new(a.Right, a.CenterY), new(a.Right + e, a.CenterY), new(a.Right + e, busY), new(b.CenterX, busY), new(b.CenterX, b.Y)], structural));
            }
        }
        foreach (var (sa, sb, arrows) in shortcuts)
        {
            var (p, q) = Geo.Nearest(Geometry.RoundRect, boxes[sa], Geometry.RoundRect, boxes[sb]);
            items.Add(new LinkItem($"Trust {sa}-{sb}", "dom:" + sa, "dom:" + sb, [p, q], other) { Arrows = arrows });
        }
        foreach (var e in exOrder)
            foreach (var (dom, arrows) in e.Links)
            {
                if (!boxes.TryGetValue(dom, out var db)) continue;
                var (p, q) = Geo.Nearest(Geometry.RoundRect, db, Geometry.RoundRect, exBoxes[e.Name]);
                items.Add(new LinkItem($"Trust {dom}-{e.Name}", "dom:" + dom, "ext:" + e.Name, [p, q], other) { Arrows = arrows });
            }
        items.AddRange(Parts.Legend(c, legend, area));
        if (force && (totalW > area.W || totalH > availH))
            c.Warnings.Add($"\"{title}\": the domains don't fit the content area even at {c.Min} pt.");
        return new SlidePlan(title, items);
    }
}
