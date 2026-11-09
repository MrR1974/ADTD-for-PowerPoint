using Adtd.Model;

namespace Adtd.Layout;

/// <summary>What one DC card shows.</summary>
internal sealed record Card(string Key, string Name, ThemeColor Color, bool ReadOnly, IReadOnlyList<string> Lines, IReadOnlyList<string> Badges);

/// <summary>
/// The size and inner geometry shared by every card on a slide at one font size (EMU). Cards on a slide are all the
/// same size, so they line up in a grid.
/// </summary>
internal sealed record CardMetrics(double FontPt, double W, double H, double Pad, double IconW, double IconH, double TextX, double TextW, double TextH,
                                   double BadgeFontPt, double BadgeH, double BadgeGap, double BadgeTop, double InnerW, int BadgeRows);

/// <summary>
/// DC cards: one group per DC holding a frame (the target connectors glue to, since they can't glue to groups), the
/// icon, the name, addresses and OS, and role badges in a fixed order.
/// </summary>
internal static class Cards
{
    public static readonly string[] BadgeOrder = ["GC", "RODC", "PDC", "RID", "Infra", "Schema", "DNM", "ISTG"];
    static readonly HashSet<string> Fsmo = ["PDC", "RID", "Infra", "Schema", "DNM"];

    public static bool IsFsmo(string badge) => Fsmo.Contains(badge);

    public static string KeyOf(DomainController dc) => "dc:" + dc.Key;

    public static Card For(Ctx c, DomainController dc, bool showIstg)
    {
        var lines = new List<string> { dc.Name };
        if (c.O.Addresses)
            lines.Add(dc.IPv4.Count switch
            {
                0 => "No IPv4 address",
                <= 2 => string.Join(", ", dc.IPv4),
                _ => $"{dc.IPv4[0]}, {dc.IPv4[1]} +{dc.IPv4.Count - 2}",
            });
        var os = OsNames.Short(dc.OperatingSystem);
        lines.Add(os.Length > 0 ? os : "OS unknown");

        var domain = c.F.Domains.FirstOrDefault(d => string.Equals(d.DnsName, dc.Domain, StringComparison.OrdinalIgnoreCase));
        bool Is(string? holder) => holder != null && string.Equals(holder, dc.Key, StringComparison.OrdinalIgnoreCase);
        var roles = new HashSet<string>();
        if (dc.IsGlobalCatalog) roles.Add("GC");
        if (dc.IsReadOnly) roles.Add("RODC");
        if (Is(domain?.PdcMaster)) roles.Add("PDC");
        if (Is(domain?.RidMaster)) roles.Add("RID");
        if (Is(domain?.InfrastructureMaster)) roles.Add("Infra");
        if (Is(c.F.SchemaMaster)) roles.Add("Schema");
        if (Is(c.F.DomainNamingMaster)) roles.Add("DNM");
        if (showIstg) roles.Add("ISTG");
        return new Card(KeyOf(dc), dc.Name, c.Colors.Of(dc.Domain), dc.IsReadOnly, lines, BadgeOrder.Where(roles.Contains).ToList());
    }

    public static CardMetrics Measure(Ctx c, IReadOnlyList<Card> cards, double f)
    {
        var m = c.M;
        var lineH = m.LineHeight(f);
        var lines = cards.Count == 0 ? 3 : cards.Max(k => k.Lines.Count);
        var textH = lineH * lines;
        var textW = cards.Count == 0 ? 0 : cards.Max(k => k.Lines.Select((l, i) => m.Width(l, f, i == 0)).Max());
        var pad = Ctx.Pt(0.6 * f);
        var iconH = textH;
        var iconW = iconH * c.IconAspect;
        var textX = pad + iconW + Ctx.Pt(0.5 * f);
        var inner = textX - pad + textW;

        var bf = Math.Max(c.Min, Math.Round(f * 0.8 * 2) / 2);
        var bh = Ctx.Pt(bf * 1.55);
        var gap = Ctx.Pt(0.3 * f);
        foreach (var k in cards)
        {
            var w = k.Badges.Select(b => BadgeWidth(c, b, bf)).ToList();
            var full = w.Sum() + gap * Math.Max(0, w.Count - 1);
            if (full <= inner) continue;
            // Two rows at most: the split whose wider row is narrowest.
            var best = full;
            for (var s = 1; s < w.Count; s++)
                best = Math.Min(best, Math.Max(w.Take(s).Sum() + gap * (s - 1), w.Skip(s).Sum() + gap * (w.Count - s - 1)));
            inner = Math.Max(inner, best);
        }
        var rows = cards.Count == 0 ? 0 : cards.Max(k => Flow(k.Badges.Select(b => BadgeWidth(c, b, bf)).ToList(), inner, gap).Count);
        var badgeTop = pad + textH + (rows > 0 ? Ctx.Pt(0.45 * f) : 0);
        var h = badgeTop + rows * bh + Math.Max(0, rows - 1) * gap + pad;
        return new CardMetrics(f, pad * 2 + inner, h, pad, iconW, iconH, textX, textW, textH, bf, bh, gap, badgeTop, inner, rows);
    }

    static double BadgeWidth(Ctx c, string badge, double bf) => c.M.Width(badge, bf, true) + Ctx.Pt(bf * 1.1);

    /// <summary>Badge widths flowed into rows no wider than <paramref name="width"/>: the index range of each row.</summary>
    static List<(int From, int To)> Flow(IReadOnlyList<double> widths, double width, double gap)
    {
        var rows = new List<(int, int)>();
        var start = 0;
        double x = 0;
        for (var i = 0; i < widths.Count; i++)
        {
            if (i > start && x + gap + widths[i] > width + 1) { rows.Add((start, i)); start = i; x = 0; }
            x += (i > start ? gap : 0) + widths[i];
        }
        if (widths.Count > start) rows.Add((start, widths.Count));
        return rows;
    }

    public static GroupItem Build(Ctx c, Card k, CardMetrics m, double x, double y)
    {
        var tx = new ThemeColor(SchemeSlot.Tx1);
        var children = new List<PlanItem>
        {
            new BoxItem($"{k.Name} frame", new Rect(x, y, m.W, m.H), Geometry.RoundRect)
            {
                Key = k.Key, Fill = new ThemeColor(SchemeSlot.Bg1), CornerRatio = 0.08,
                Line = new Stroke(k.Color, k.ReadOnly ? 1.5 : 1.75, k.ReadOnly ? Dash.Dash : Dash.Solid),
            },
            new IconItem($"{k.Name} icon", new Rect(x + m.Pad, y + m.Pad, m.IconW, m.IconH)),
            new TextItem($"{k.Name} details", new Rect(x + m.TextX, y + m.Pad, m.TextW, m.TextH),
                PlanText.Lines(k.Lines, m.FontPt, tx, boldFirst: true) with { Anchor = VAnchor.Top, Inset = 0, Wrap = false }),
        };
        var widths = k.Badges.Select(b => BadgeWidth(c, b, m.BadgeFontPt)).ToList();
        var by = y + m.BadgeTop;
        foreach (var (from, to) in Flow(widths, m.InnerW, m.BadgeGap))
        {
            var bx = x + m.Pad;
            for (var i = from; i < to; i++)
            {
                children.Add(Badge(c, k, k.Badges[i], new Rect(bx, by, widths[i], m.BadgeH), m.BadgeFontPt));
                bx += widths[i] + m.BadgeGap;
            }
            by += m.BadgeH + m.BadgeGap;
        }
        return new GroupItem($"DC {k.Name}", children);
    }

    static BoxItem Badge(Ctx c, Card k, string badge, Rect r, double f)
    {
        ColorRef? fill = null;
        Stroke? line = null;
        ColorRef text = new ThemeColor(SchemeSlot.Tx1);
        if (badge == "GC") { var g = k.Color.Darker(0.25); fill = g; text = c.Theme.TextOn(g); }
        else if (Fsmo.Contains(badge)) { fill = new ThemeColor(SchemeSlot.Tx2); text = c.Theme.TextOn(fill); }
        else if (badge == "RODC") line = new Stroke(new ThemeColor(SchemeSlot.Tx1), 1, Dash.Dash);
        else line = new Stroke(new ThemeColor(SchemeSlot.Tx2), 1);
        return new BoxItem($"{k.Name} {badge}", r, Geometry.RoundRect)
        {
            Fill = fill, Line = line, CornerRatio = 0.5,
            Text = PlanText.Simple(badge, f, text, true, HAlign.Center) with { Inset = 0, Wrap = false },
        };
    }
}

/// <summary>Fits a set of equal-sized cards into an area as a grid, at the largest font that fits.</summary>
internal static class Grid
{
    /// <summary>The largest card font: 1.6 × the 10 pt base.</summary>
    public const double MaxFont = 16;

    public sealed record Fit(CardMetrics M, int Cols, int Rows, double Gap)
    {
        public double Width => Cols * M.W + (Cols - 1) * Gap;
        public double Height => Rows * M.H + (Rows - 1) * Gap;
    }

    public static double GapFor(double f) => Ctx.Pt(0.8 * f);

    /// <summary>
    /// The largest font (in 0.5 pt steps, at most <paramref name="maxFont"/>) at which the cards fit, and the column
    /// count whose shape best matches the area's. Null if they don't fit even at the minimum size.
    /// </summary>
    public static Fit? Find(Ctx c, IReadOnlyList<Card> cards, double w, double h, double maxFont = MaxFont)
    {
        var n = cards.Count;
        if (n == 0) return null;
        for (var f = maxFont; f >= c.Min - 1e-9; f -= 0.5)
        {
            var m = Cards.Measure(c, cards, f);
            var g = GapFor(f);
            Fit? best = null;
            var bestScore = double.MaxValue;
            for (var cols = 1; cols <= n; cols++)
            {
                var fit = new Fit(m, cols, (n + cols - 1) / cols, g);
                if (fit.Width > w || fit.Height > h) continue;
                var score = Math.Abs(Math.Log(fit.Width / fit.Height / (w / h)));
                if (score < bestScore) { best = fit; bestScore = score; }
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>How many of these cards fit in the area at the minimum size.</summary>
    public static int Capacity(Ctx c, IReadOnlyList<Card> cards, double w, double h)
    {
        var m = Cards.Measure(c, cards, c.Min);
        var g = GapFor(c.Min);
        var cols = (int)Math.Floor((w + g) / (m.W + g));
        var rows = (int)Math.Floor((h + g) / (m.H + g));
        return Math.Max(0, cols) * Math.Max(0, rows);
    }

    /// <summary>The cards in rows, left to right, from (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static List<PlanItem> Place(Ctx c, IReadOnlyList<Card> cards, Fit fit, double x, double y) =>
        cards.Select((k, i) => (PlanItem)Cards.Build(c, k, fit.M, x + i % fit.Cols * (fit.M.W + fit.Gap), y + i / fit.Cols * (fit.M.H + fit.Gap))).ToList();

    /// <summary>Splits items into the fewest chunks of at most <paramref name="size"/>, as even as possible.</summary>
    public static List<List<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        size = Math.Max(1, size);
        var n = (items.Count + size - 1) / size;
        var result = new List<List<T>>();
        for (int i = 0, start = 0; i < n; i++)
        {
            var len = (items.Count - start) / (n - i) + ((items.Count - start) % (n - i) > 0 ? 1 : 0);
            result.Add(items.Skip(start).Take(len).ToList());
            start += len;
        }
        return result;
    }
}
