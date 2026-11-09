using Adtd.Model;

namespace Adtd.Layout;

/// <summary>
/// The sites and site links, placed by ADTD.Net's <see cref="WebLayout"/> and built the way ADTD.Net built the diagram:
/// every site a node, a link between two sites an edge, and a link with more sites (or one) a small connection point
/// joined to each of its sites. Sites are rounded rectangles, all drawn alike; each link is labelled with its name, cost
/// and interval. The layout is scaled to the content area (across and down separately, neither more than twice the
/// other, and never more than 1.5× ADTD.Net's own scale), at the largest font where no two shapes overlap. If none fits, the sites are split over several slides by cluster (the sites that hang together
/// once the multi-site links are set aside), each slide laid out the same way. A site (or connection point) with links to
/// sites on other slides gets a dashed "+n more sites" placeholder saying which slides they're on.
/// With <see cref="DeckOptions.ConsolidateEmptySites"/>, empty sites are aggregated the way ADTD.Net's DoAggregate was
/// written to (it is commented out in ADTD.Net's source): sites with no DCs and one neighbour, grouped by that
/// neighbour, each group of two or more replaced by one "Aggregate Sites-n" box joined to the neighbour by one link.
/// </summary>
internal static class SitesOverview
{
    /// <summary><see cref="Aggregated"/> is the number of empty sites an aggregate box stands for (0 for anything else).</summary>
    sealed record Node(int Id, string Name, string Key, Site? Site, SiteLink? Point, int Dcs, int Aggregated = 0);
    /// <summary>An aggregated link is <see cref="Uniform"/> when the links it stands for share a cost and interval.</summary>
    sealed record Edge(int From, int To, SiteLink Link, bool Aggregate = false, bool Uniform = true);

    sealed class Graph
    {
        public List<Node> Nodes { get; } = [];
        public List<Edge> Edges { get; } = [];
        public Dictionary<string, Point[]> Positions { get; } = [];
    }

    const double EmuPerMm = 36000;
    /// <summary>ADTD.Net's connection point is 5 × 5 (mm).</summary>
    const double PointMm = 5;
    /// <summary>The most the layout is enlarged beyond ADTD.Net's own scale.</summary>
    const double MaxScale = 1.5;

    public static IEnumerable<SlidePlan> Plan(Ctx c)
    {
        var g = Build(c);
        var sites = g.Nodes.Where(n => n.Point == null).Select(n => n.Id).ToList();
        const string title = "Sites and site links";
        foreach (var f in c.Ladder(14, 12, 10, 9))
            if (Attempt(c, g, sites, f, title, force: false) is { } whole) return [whole];

        var pages = Split(c, g, sites);
        var crossing = g.Edges.Count(e => g.Nodes[e.From].Point == null && g.Nodes[e.To].Point == null &&
                                          !pages.Any(p => p.Contains(e.From) && p.Contains(e.To)));
        var repeated = pages.SelectMany(p => p).GroupBy(v => v).Where(x => x.Count() > 1).Select(x => g.Nodes[x.Key].Name).ToList();
        c.Warnings.Add($"The sites slide is split into {pages.Count} slides" +
                       (repeated.Count > 0 ? $" (repeating {string.Join(", ", repeated)} on each slide of its cluster)" : "") +
                       (crossing > 0 ? $"; {Ctx.Count(crossing, "site link joins", "site links join")} sites on different slides (shown by \"more sites\" placeholders)." : "."));
        return pages.Select((p, i) =>
        {
            var t = $"{title} ({i + 1}/{pages.Count})";
            var where = new Pages(pages, i);
            return c.Ladder(14, 12, 10, 9).Select(f => Attempt(c, g, p, f, t, false, where)).FirstOrDefault(s => s != null) ?? Attempt(c, g, p, c.Min, t, true, where)!;
        }).ToList();
    }

    /// <summary>The split, and which of its slides is being laid out: lets a placeholder say where the missing sites are.</summary>
    sealed record Pages(List<List<int>> All, int Current)
    {
        /// <summary>"2", "2 and 4", "2–5" or "2, 4–6": the slides (numbered as in their titles), other than this one, holding any of <paramref name="sites"/>.</summary>
        public string Where(IEnumerable<int> sites)
        {
            var set = sites.ToHashSet();
            var nums = All.Select((p, i) => (p, i)).Where(x => x.i != Current && x.p.Any(set.Contains)).Select(x => x.i + 1).ToList();
            var parts = new List<string>();
            for (var k = 0; k < nums.Count;)
            {
                var j = k;
                while (j + 1 < nums.Count && nums[j + 1] == nums[j] + 1) j++;
                parts.Add(j == k ? $"{nums[k]}" : j == k + 1 ? $"{nums[k]}, {nums[j]}" : $"{nums[k]}–{nums[j]}");
                k = j + 1;
            }
            return parts.Count == 2 ? $"{parts[0]} and {parts[1]}" : string.Join(", ", parts);
        }

        public int Total => All.Count;
    }

    /// <summary>Nodes and edges in ADTD.Net's order: sites, then a connection point per link of other than two sites, as links are read.</summary>
    static Graph Build(Ctx c)
    {
        var g = new Graph();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in c.Sites)
        {
            index[s.Name] = g.Nodes.Count;
            g.Nodes.Add(new Node(g.Nodes.Count, s.Name, "site:" + s.Name, s, null, c.DcsIn(s.Name).Count));
        }
        foreach (var l in c.F.SiteLinks)
        {
            var members = l.Sites.Where(index.ContainsKey).Select(s => index[s]).Distinct().ToList();
            if (members.Count == 0) continue;
            if (members.Count == 2) { g.Edges.Add(new Edge(members[0], members[1], l)); continue; }
            var cp = new Node(g.Nodes.Count, l.Name, "hub:" + l.Name, null, l, 0);
            g.Nodes.Add(cp);
            foreach (var m in members) g.Edges.Add(new Edge(m, cp.Id, l));
        }
        return c.O.ConsolidateEmptySites ? Aggregate(g) : g;
    }

    /// <summary>
    /// ADTD.Net's DoAggregate: sites with no DCs connected to exactly one node, grouped by that node; each group of more
    /// than one is removed with its links and replaced by an "Aggregate Sites-n" node with one "Aggregated SiteLink" to
    /// that node. The other nodes keep their order; the aggregates are added last, as ADTD.Net added them.
    /// </summary>
    static Graph Aggregate(Graph g)
    {
        int Other(Edge e, int v) => e.From == v ? e.To : e.From;
        var groups = g.Nodes.Where(n => n.Point == null && n.Dcs == 0)
                            .Select(n => (n.Id, Near: g.Edges.Where(e => e.From == n.Id || e.To == n.Id).Select(e => Other(e, n.Id)).Distinct().ToList()))
                            .Where(x => x.Near.Count == 1)
                            .GroupBy(x => x.Near[0], x => x.Id)
                            .Where(gr => gr.Count() > 1)
                            .OrderBy(gr => gr.Min())
                            .ToList();
        if (groups.Count == 0) return g;

        var removed = groups.SelectMany(gr => gr).ToHashSet();
        var result = new Graph();
        var map = new Dictionary<int, int>();
        foreach (var n in g.Nodes.Where(n => !removed.Contains(n.Id)))
        {
            map[n.Id] = result.Nodes.Count;
            result.Nodes.Add(n with { Id = result.Nodes.Count });
        }
        foreach (var e in g.Edges.Where(e => !removed.Contains(e.From) && !removed.Contains(e.To)))
            result.Edges.Add(e with { From = map[e.From], To = map[e.To] });
        var k = 0;
        foreach (var gr in groups)
        {
            k++;
            var members = gr.ToHashSet();
            var links = g.Edges.Where(e => members.Contains(e.From) || members.Contains(e.To)).Select(e => e.Link).ToList();
            var first = links[0];
            var uniform = links.All(l => l.Cost == first.Cost && l.ReplicationIntervalMinutes == first.ReplicationIntervalMinutes);
            var alike = links.All(l => l.Options == first.Options && l.ScheduleHours == first.ScheduleHours && l.Transport == first.Transport);
            var link = new SiteLink
            {
                Name = "Aggregated SiteLink", Cost = first.Cost, ReplicationIntervalMinutes = first.ReplicationIntervalMinutes,
                Options = alike ? first.Options : 0, ScheduleHours = alike ? first.ScheduleHours : null, Transport = alike ? first.Transport : "IP",
                Sites = gr.Select(v => g.Nodes[v].Name).ToList(),
            };
            var agg = new Node(result.Nodes.Count, $"Aggregate Sites-{k}", $"agg:{k}", null, null, 0, members.Count);
            result.Nodes.Add(agg);
            // From the aggregate to its neighbour, as a site is joined to a connection point.
            result.Edges.Add(new Edge(agg.Id, map[gr.Key], link, Aggregate: true, Uniform: uniform));
        }
        return result;
    }

    /// <summary>
    /// Clusters (sites joined by two-site links) filled onto slides while they still fit at 10 pt. A cluster too big
    /// for a slide on its own is shared out round its hub (its best-linked site): the hub is repeated on each of its
    /// slides with as many of the other sites, nearest first, as fit, so every link to the hub is still drawn.
    /// </summary>
    static List<List<int>> Split(Ctx c, Graph g, List<int> sites)
    {
        var adj = sites.ToDictionary(s => s, _ => new List<int>());
        foreach (var e in g.Edges.Where(e => g.Nodes[e.From].Point == null && g.Nodes[e.To].Point == null))
        {
            adj[e.From].Add(e.To);
            adj[e.To].Add(e.From);
        }
        List<int> Bfs(int start, ISet<int> within)
        {
            var order = new List<int>();
            var seen = new HashSet<int> { start };
            var q = new Queue<int>([start]);
            while (q.Count > 0)
            {
                var v = q.Dequeue();
                order.Add(v);
                foreach (var w in adj[v].Where(within.Contains).Order()) if (seen.Add(w)) q.Enqueue(w);
            }
            return order;
        }
        var all = sites.ToHashSet();
        var clusters = new List<List<int>>();
        var done = new HashSet<int>();
        foreach (var s in sites.Where(s => !done.Contains(s)))
        {
            var cl = Bfs(s, all);
            done.UnionWith(cl);
            clusters.Add(cl);
        }

        var target = Math.Max(c.Min, 10);
        var queue = new LinkedList<List<int>>(clusters);
        var pages = new List<List<int>>();
        var cur = new List<int>();
        while (queue.Count > 0)
        {
            var cl = queue.First!.Value;
            queue.RemoveFirst();
            if (Attempt(c, g, [.. cur, .. cl], target, "", false) != null) { cur.AddRange(cl); continue; }
            if (cur.Count > 0) { pages.Add(cur); cur = []; queue.AddFirst(cl); continue; }
            pages.AddRange(Alone(cl));
        }
        if (cur.Count > 0) pages.Add(cur);
        return pages;

        // A cluster that has a slide to itself: one slide if it fits at all, otherwise shared out round its hub.
        List<List<int>> Alone(List<int> cl) =>
            cl.Count == 1 || Attempt(c, g, cl, c.Min, "", false) != null ? [cl] : RoundHub(cl);

        // The hub (the best-linked site) is repeated on each slide, with the groups that hang off it (what's left joined
        // up once the hub is taken out), nearest first, as many as fit. A group too big to go with the hub is shared out
        // on its own slides, the same way; a "more sites" placeholder there points back to the hub.
        List<List<int>> RoundHub(List<int> cl)
        {
            var members = cl.ToHashSet();
            var hub = cl.MaxBy(v => adj[v].Count(members.Contains));
            var rest = members.Where(v => v != hub).ToHashSet();
            var groups = new List<List<int>>();
            foreach (var v in Bfs(hub, members).Skip(1).Concat(cl).Where(v => v != hub).Distinct())
                if (!groups.Any(gr => gr.Contains(v))) groups.Add(Bfs(v, rest));
            var cur = new List<int> { hub };
            bool Fits(List<int> page, int from, int n) => Attempt(c, g, [.. page, .. groups.Skip(from).Take(n).SelectMany(x => x)], target, "", false) != null;
            var withHub = new List<List<int>>();
            var alone = new List<List<int>>();
            for (var i = 0; i < groups.Count;)
            {
                // The most groups that fit on the hub's current slide: grow by doubling, then bisect.
                int left = groups.Count - i, lo = 0, hi = 1;
                while (hi <= left && Fits(cur, i, hi)) { lo = hi; hi *= 2; }
                hi = Math.Min(hi, left + 1);
                while (hi - lo > 1) { var mid = (lo + hi) / 2; if (Fits(cur, i, mid)) lo = mid; else hi = mid; }
                cur.AddRange(groups.Skip(i).Take(lo).SelectMany(x => x));
                i += lo;
                if (i == groups.Count) break;
                // The next group doesn't fit here: start a new slide for it if it fits with the hub, or else give it its
                // own slides and carry on filling this one.
                if (cur.Count > 1 && Fits([hub], i, 1)) { withHub.Add(cur); cur = [hub]; }
                else alone.AddRange(Alone(groups[i++]));
            }
            if (cur.Count > 1 || withHub.Count == 0) withHub.Add(cur);
            return [.. withHub, .. alone];
        }
    }

    /// <summary>One slide of the given sites (and the connection points that reach them) at font <paramref name="f"/>; null if it doesn't fit.</summary>
    static SlidePlan? Attempt(Ctx c, Graph g, List<int> pageSites, double f, string title, bool force, Pages? where = null)
    {
        var area = c.Content;
        var m = c.M;
        var sitesOnPage = pageSites.ToHashSet();
        var points = g.Nodes.Where(n => n.Point != null && g.Edges.Any(e => e.To == n.Id && sitesOnPage.Contains(e.From))).Select(n => n.Id);
        var nodes = g.Nodes.Where(n => sitesOnPage.Contains(n.Id)).Select(n => n.Id).Concat(points).ToList();   // ADTD.Net's order
        var local = nodes.Select((v, i) => (v, i)).ToDictionary(t => t.v, t => t.i);
        var edges = g.Edges.Where(e => local.ContainsKey(e.From) && local.ContainsKey(e.To)).ToList();

        // Placeholders: a node on this slide with links to sites that aren't gets one, joined to it, standing for them.
        // While the split is being worked out (no where) it's sized for the widest text it could have.
        var away = new List<(int Owner, List<int> Sites)>();
        foreach (var v in nodes)
        {
            var off = g.Edges.Where(e => e.From == v || e.To == v).Select(e => e.From == v ? e.To : e.From)
                             .Where(o => g.Nodes[o].Point == null && !sitesOnPage.Contains(o)).Distinct().Order().ToList();
            if (off.Count > 0) away.Add((v, off));
        }

        // Shapes and their sizes.
        var lf = Math.Max(c.Min, f - 2);
        var ins = Ctx.Pt(0.45 * f);
        var lineH = m.LineHeight(f);
        var siteH = 2 * lineH + 2 * ins;
        var dot = Math.Max(Ctx.Pt(7), PointMm * EmuPerMm);
        var siteFill = new ThemeColor(SchemeSlot.Tx2).Lighter(0.82);
        var siteText = c.Theme.TextOn(siteFill);
        var size = new Dictionary<int, (double W, double H)>();
        foreach (var v in nodes)
        {
            var n = g.Nodes[v];
            size[v] = n.Point != null ? (dot, dot)
                : (Math.Max(m.Width(n.Name, f, true), m.Width(DcText(n), f)) + 2 * ins, siteH);
        }
        // Placeholders have negative ids: -1 - their index in away.
        var awayText = new List<string[]>();
        for (var k = 0; k < away.Count; k++)
        {
            var lines = AwayLines(away[k].Sites.Count, where?.Where(away[k].Sites), where?.Total ?? 99);
            awayText.Add(lines);
            size[-1 - k] = (Math.Max(m.Width(lines[0], f, true), m.Width(lines[1], f)) + 2 * ins, siteH);
        }
        var all = nodes.Concat(Enumerable.Range(0, away.Count).Select(k => -1 - k)).ToList();

        // ADTD.Net's geometry, in mm (connection points at ADTD.Net's 5 × 5).
        var key = $"{f}|{string.Join(",", nodes)}|{string.Join(",", Enumerable.Range(0, away.Count).Select(k => $"{away[k].Owner}:{size[-1 - k].W:0}"))}";
        if (!g.Positions.TryGetValue(key, out var pos))
        {
            var mm = all.Select(v => v >= 0 && g.Nodes[v].Point != null ? (PointMm, PointMm) : (size[v].W / EmuPerMm, size[v].H / EmuPerMm)).ToList();
            var pairs = edges.Select(e => (local[e.From], local[e.To])).Concat(away.Select((a, k) => (local[a.Owner], nodes.Count + k))).ToList();
            pos = WebLayout.Arrange(mm, pairs);
            g.Positions[key] = pos;
        }

        var legend = Legend(c, g, nodes, edges, siteFill, away.Count > 0);
        var lh = Parts.LegendHeight(c, legend.Count);
        var avail = new Rect(area.X, area.Y, area.W, area.H - lh);
        double minX = pos.Min(p => p.X), maxX = pos.Max(p => p.X), minY = pos.Min(p => p.Y), maxY = pos.Max(p => p.Y);
        double maxW = size.Values.Max(s => s.W), maxH = size.Values.Max(s => s.H);
        // Across and down are scaled separately to suit the slide's shape, but neither more than twice the other.
        double sx = MaxScale * EmuPerMm, sy = sx;
        if (maxX > minX) sx = Math.Min(sx, (avail.W - maxW) / (maxX - minX));
        if (maxY > minY) sy = Math.Min(sy, (avail.H - maxH) / (maxY - minY));
        sx = Math.Min(sx, 2 * sy);
        sy = Math.Min(sy, 2 * sx);
        if (sx <= 0 || sy <= 0) { if (!force) return null; sx = sy = 1; }
        var ox = avail.CenterX - (minX + maxX) / 2 * sx;
        var oy = avail.CenterY - (minY + maxY) / 2 * sy;
        var rects = new Dictionary<int, Rect>();
        for (var i = 0; i < all.Count; i++)
        {
            var (w, h) = size[all[i]];
            rects[all[i]] = new Rect(ox + pos[i].X * sx - w / 2, oy + pos[i].Y * sy - h / 2, w, h);
        }

        // Fits when no two shapes touch.
        var gap = Ctx.Pt(4);
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
                if (rects[all[i]].Inset(-gap / 2).Intersects(rects[all[j]].Inset(-gap / 2)) && !force) return null;

        var items = new List<PlanItem>();
        foreach (var v in nodes)
        {
            var n = g.Nodes[v];
            items.Add(n.Point != null
                ? new BoxItem($"Site link {n.Name}", rects[v], Geometry.Ellipse) { Key = n.Key, Fill = new ThemeColor(SchemeSlot.Tx2) }
                : new BoxItem($"Site {n.Name}", rects[v], Geometry.RoundRect)
                {
                    Key = n.Key, Fill = siteFill, Line = new Stroke(new ThemeColor(SchemeSlot.Tx2), 1), CornerRatio = 0.2,
                    Text = PlanText.Lines([n.Name, DcText(n)], f, siteText, boldFirst: true, HAlign.Center) with { Inset = ins, Wrap = false },
                });
        }
        // Placeholders: the background colour inside a dashed outline, so they don't read as sites.
        var awayStroke = new Stroke(new ThemeColor(SchemeSlot.Tx2), 1, Dash.LongDash);
        for (var k = 0; k < away.Count; k++)
        {
            var owner = g.Nodes[away[k].Owner];
            items.Add(new BoxItem($"More sites from {owner.Name}", rects[-1 - k], Geometry.RoundRect)
            {
                Key = $"away:{owner.Key}", Fill = new ThemeColor(SchemeSlot.Bg1), Line = awayStroke, CornerRatio = 0.2,
                Text = PlanText.Lines(awayText[k], f, new ThemeColor(SchemeSlot.Tx1), boldFirst: true, HAlign.Center) with { Inset = ins, Wrap = false },
            });
        }

        // Connectors, glued to the sites and connection points.
        var routes = new List<(Point A, Point B)>();
        foreach (var e in edges)
        {
            Node a = g.Nodes[e.From], b = g.Nodes[e.To];
            var r = Geo.Nearest(a.Point != null ? Geometry.Ellipse : Geometry.RoundRect, rects[e.From], b.Point != null ? Geometry.Ellipse : Geometry.RoundRect, rects[e.To]);
            routes.Add(r);
            items.Add(new LinkItem($"Site link {e.Link.Name}{(b.Point != null ? " to " + a.Name : "")}", a.Key, b.Key, [r.A, r.B], LinkStroke(e.Link)));
        }
        for (var k = 0; k < away.Count; k++)
        {
            var owner = g.Nodes[away[k].Owner];
            var r = Geo.Nearest(owner.Point != null ? Geometry.Ellipse : Geometry.RoundRect, rects[away[k].Owner], Geometry.RoundRect, rects[-1 - k]);
            items.Add(new LinkItem($"Site links from {owner.Name} to other slides", owner.Key, $"away:{owner.Key}", [r.A, r.B], awayStroke));
        }

        // Labels: name, then cost and interval. Two-site links along their line; connection points beside the point.
        var placed = new List<Rect>();
        var dropped = 0;
        var labelled = 0;
        bool Free(Rect r)
        {
            var pad = r.Inset(-Ctx.Pt(2));
            return avail.Contains(r) && !rects.Values.Any(o => o.Intersects(pad)) && !placed.Any(o => o.Intersects(pad));
        }
        void Label(string name, List<string> lines, IEnumerable<Point> centres, double w, double h)
        {
            labelled++;
            foreach (var p in centres)
            {
                var r = new Rect(p.X - w / 2, p.Y - h / 2, w, h);
                if (!Free(r)) continue;
                placed.Add(r);
                // Filled with the background, so a connector doesn't run through the text.
                items.Add(new BoxItem(name, r, Geometry.Rect)
                {
                    Fill = new ThemeColor(SchemeSlot.Bg1),
                    Text = PlanText.Lines(lines, lf, new ThemeColor(SchemeSlot.Tx1), boldFirst: true, HAlign.Center) with { Inset = 0, Wrap = false },
                });
                return;
            }
            dropped++;
        }
        var labelH = 2 * m.LineHeight(lf);
        if (c.O.LinkLabels)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (g.Nodes[e.To].Point != null) continue;
                var lines = e.Aggregate && !e.Uniform ? [e.Link.Name, $"{Ctx.Count(e.Link.Sites.Count, "link", "links")}"] : LabelLines(e.Link, null);
                var w = Math.Max(m.Width(lines[0], lf, true), m.Width(lines[1], lf)) + Ctx.Pt(3);
                var (p, q) = routes[i];
                var len = Math.Max(1, Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y)));
                double nx = -(q.Y - p.Y) / len, ny = (q.X - p.X) / len;
                // Nearest the middle first, on the line, then either side of it.
                var steps = Enumerable.Range(0, 11).Select(k => 0.5 + (k % 2 == 0 ? 1 : -1) * (k + 1) / 2 * 0.075);
                var centres = from off in new[] { 0.0, 0.7, -0.7, 1.4, -1.4 }
                              from t in steps
                              select new Point(p.X + (q.X - p.X) * t + nx * off * labelH, p.Y + (q.Y - p.Y) * t + ny * off * labelH);
                Label($"Site link {e.Link.Name} label", lines, centres, w, labelH);
            }
        }
        foreach (var v in nodes.Where(v => g.Nodes[v].Point != null))
        {
            var n = g.Nodes[v];
            var shown = edges.Count(e => e.To == v);
            var lines = c.O.LinkLabels ? LabelLines(n.Point!, shown < n.Point!.Sites.Count ? n.Point.Sites.Count : null) : [n.Name];
            var w = lines.Select((s, i) => m.Width(s, lf, i == 0)).Max() + Ctx.Pt(3);
            var h = lines.Count * m.LineHeight(lf);
            var r = rects[v];
            var d = Ctx.Pt(3);
            Point[] centres =
            [
                new(r.Right + d + w / 2, r.CenterY), new(r.X - d - w / 2, r.CenterY), new(r.CenterX, r.Y - d - h / 2), new(r.CenterX, r.Bottom + d + h / 2),
                new(r.Right + d + w / 2, r.Y - h / 2), new(r.X - d - w / 2, r.Y - h / 2), new(r.Right + d + w / 2, r.Bottom + h / 2), new(r.X - d - w / 2, r.Bottom + h / 2),
            ];
            Label($"Site link {n.Name} label", lines, centres, w, h);
        }
        if (!force && dropped > Math.Max(1, labelled / 5)) return null;
        if (dropped > 0 && title.Length > 0) c.Warnings.Add($"\"{title}\": {Ctx.Count(dropped, "site link label", "site link labels")} left out for lack of room.");
        items.AddRange(Parts.Legend(c, legend, area));
        if (force && title.Length > 0) c.Warnings.Add($"\"{title}\": the sites don't fit the content area even at {c.Min} pt.");
        return new SlidePlan(title, items);
    }

    static string DcText(Node n) => n.Aggregated > 0 ? $"{n.Aggregated} empty sites" : n.Dcs == 0 ? "No DCs" : Ctx.Count(n.Dcs, "DC", "DCs");

    /// <summary>"+12 more sites" over "on slides 2–5" (or "on slide 3"); while the split is being worked out, the widest that could be.</summary>
    static string[] AwayLines(int count, string? where, int total)
    {
        var first = count == 1 ? "+1 more site" : $"+{count} more sites";
        if (where == null) return [first, $"on slides {total}–{total}"];
        return [first, where.Length == 0 ? "on other slides" : int.TryParse(where, out _) ? $"on slide {where}" : $"on slides {where}"];
    }

    static List<string> LabelLines(SiteLink l, int? totalSites) =>
        [l.Name, $"{l.Cost} · {l.ReplicationIntervalMinutes} min" + (totalSites is int t ? $" · {t} sites" : "")];

    static Stroke LinkStroke(SiteLink l) =>
        new(new ThemeColor(SchemeSlot.Tx2), l.ChangeNotification ? 2.25 : 1.25,
            l.Transport.Equals("SMTP", StringComparison.OrdinalIgnoreCase) ? Dash.Dot : l.ScheduleHours is < 168 ? Dash.Dash : Dash.Solid);

    static List<LegendEntry> Legend(Ctx c, Graph g, List<int> nodes, List<Edge> edges, ColorRef siteFill, bool away)
    {
        var tx2 = new ThemeColor(SchemeSlot.Tx2);
        var entries = new List<LegendEntry> { new("Site", Geometry.RoundRect, siteFill, new Stroke(tx2, 1)) };
        if (away) entries.Add(new("Sites on other slides", Geometry.RoundRect, new ThemeColor(SchemeSlot.Bg1), new Stroke(tx2, 1, Dash.LongDash)));
        if (nodes.Any(v => g.Nodes[v].Point != null))
            entries.Add(new("Site link joining 3+ sites", Geometry.Ellipse, tx2, null));
        if (edges.Count > 0)
            entries.Add(new(c.O.LinkLabels ? "Site link: name, cost · replication interval" : "Site link", Geometry.Rect, null, new Stroke(tx2, 1.25), IsLine: true));
        if (edges.Any(e => e.Link.ChangeNotification))
            entries.Add(new("Change notification on", Geometry.Rect, null, new Stroke(tx2, 2.25), IsLine: true));
        if (edges.Any(e => e.Link.ScheduleHours is < 168))
            entries.Add(new("Limited schedule", Geometry.Rect, null, new Stroke(tx2, 1.25, Dash.Dash), IsLine: true));
        return entries;
    }
}
