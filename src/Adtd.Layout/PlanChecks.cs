namespace Adtd.Layout;

/// <summary>
/// The layout invariants: everything inside the content area, no two shapes overlapping, no text below the minimum
/// size or too big for its box, and every connector end on a shape that's on the slide. Returns what's wrong, in words.
/// </summary>
public static class PlanChecks
{
    /// <summary>Slack for rounding, EMU (about 0.001 in).</summary>
    const double Tolerance = 1000;

    public static List<string> Check(DeckPlan plan, SlideFrame frame, ITextMeasurer measurer, double minFontPt)
    {
        var problems = new List<string>();
        foreach (var slide in plan.Slides) Check(slide, frame.Content, measurer, minFontPt, problems);
        return problems;
    }

    static void Check(SlidePlan slide, Rect content, ITextMeasurer m, double minFont, List<string> problems)
    {
        void Problem(string what) => problems.Add($"\"{slide.Title}\": {what}");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        void Walk(PlanItem item)
        {
            if (item is BoxItem { Key: { } k }) keys.Add(k);
            if (item is GroupItem g) foreach (var c in g.Children) Walk(c);
        }
        foreach (var item in slide.Items) Walk(item);

        void Visit(PlanItem item)
        {
            switch (item)
            {
                case GroupItem g:
                    foreach (var c in g.Children) Visit(c);
                    Overlaps(g.Children.OfType<GroupItem>().Cast<PlanItem>().ToList(), Problem);
                    break;
                case LinkItem l:
                    if (!keys.Contains(l.FromKey)) Problem($"{l.Name} starts on {l.FromKey}, which isn't on the slide.");
                    if (!keys.Contains(l.ToKey)) Problem($"{l.Name} ends on {l.ToKey}, which isn't on the slide.");
                    foreach (var p in l.Route)
                        if (!content.Contains(new Rect(p.X, p.Y, 0, 0), Tolerance)) { Problem($"{l.Name} runs outside the content area."); break; }
                    break;
                case TableItem t:
                    Inside(t.Name, t.Bounds);
                    Table(t);
                    break;
                case BoxItem b:
                    Inside(b.Name, b.Bounds);
                    if (b.Text != null) Text(b.Name, b.Geometry == Geometry.Ellipse ? EllipseInner(b.Bounds) : b.Bounds, b.Text);
                    break;
                case TextItem t:
                    Inside(t.Name, t.Bounds);
                    Text(t.Name, t.Bounds, t.Text);
                    break;
                case IconItem i:
                    Inside(i.Name, i.Bounds);
                    break;
            }
        }
        foreach (var item in slide.Items) Visit(item);
        Overlaps(slide.Items.Where(i => i is not LinkItem).ToList(), Problem);

        void Inside(string name, Rect r)
        {
            if (!content.Contains(r, Tolerance)) Problem($"{name} {r} is outside the content area {content}.");
        }

        void Text(string name, Rect r, PlanText t)
        {
            var inner = r.Inset(t.Inset);
            double h = 0;
            foreach (var p in t.Paras)
            {
                var size = p.Runs.Select(x => x.SizePt ?? t.SizePt).DefaultIfEmpty(t.SizePt).Max();
                if (size < minFont - 1e-9) Problem($"{name} has {size} pt text, below the {minFont} pt minimum.");
                var w = p.Runs.Sum(x => m.Width(x.Text, x.SizePt ?? t.SizePt, x.Bold, t.Font));
                if (w > inner.W + Tolerance) Problem($"{name}: \"{string.Concat(p.Runs.Select(x => x.Text))}\" is wider than its box.");
                h += m.LineHeight(size, t.Font);
            }
            if (t.Paras.Count == 0 && t.SizePt < minFont - 1e-9) Problem($"{name} has {t.SizePt} pt text, below the {minFont} pt minimum.");
            if (h > inner.H + Tolerance) Problem($"{name}: its text is taller than its box.");
        }

        void Table(TableItem t)
        {
            if (t.FontPt < minFont - 1e-9) Problem($"{t.Name} has {t.FontPt} pt text, below the {minFont} pt minimum.");
            var cols = Math.Max(t.Header.Count, t.Rows.Count == 0 ? 0 : t.Rows.Max(r => r.Count));
            var weights = t.ColumnWeights is { Count: > 0 } wts && wts.Count == cols ? wts : Enumerable.Repeat(1.0, cols).ToList();
            var total = weights.Sum();
            var ins = Math.Max(2, t.FontPt * 0.4) * Emu.PerPoint;
            for (var j = 0; j < cols; j++)
            {
                var colW = t.Bounds.W * weights[j] / total - 2 * ins;
                var widest = Math.Max(j < t.Header.Count ? m.Width(t.Header[j], t.FontPt, true) : 0,
                                      t.Rows.Select(r => j < r.Count ? m.Width(r[j], t.FontPt) : 0).DefaultIfEmpty(0).Max());
                if (widest > colW + Tolerance) Problem($"{t.Name}: column {j + 1} text is wider than the column.");
            }
            if (t.RowHeight is double rh && rh < m.LineHeight(t.FontPt) + 2 * Emu.PerPoint) Problem($"{t.Name}: rows are too short for their text.");
        }
    }

    /// <summary>The text area PowerPoint uses inside an ellipse.</summary>
    static Rect EllipseInner(Rect r) => r.Inset(r.W * (1 - Math.Sqrt(0.5)) / 2, r.H * (1 - Math.Sqrt(0.5)) / 2);

    static void Overlaps(List<PlanItem> items, Action<string> problem)
    {
        var bounds = items.Select(i => (i.Name, R: BoundsOf(i))).Where(x => x.R is Rect r && r.W > 0 && r.H > 0).Select(x => (x.Name, R: x.R!.Value)).ToList();
        for (var i = 0; i < bounds.Count; i++)
            for (var j = i + 1; j < bounds.Count; j++)
                if (bounds[i].R.Inset(Tolerance).Intersects(bounds[j].R.Inset(Tolerance)))
                    problem($"{bounds[i].Name} overlaps {bounds[j].Name}.");
    }

    public static Rect? BoundsOf(PlanItem item) => item switch
    {
        BoxItem b => b.Bounds,
        TextItem t => t.Bounds,
        IconItem i => i.Bounds,
        TableItem t => t.Bounds,
        GroupItem g => g.Children.Select(BoundsOf).OfType<Rect>().Aggregate((Rect?)null, (acc, r) => acc is Rect a ? Rect.Union(a, r) : r),
        _ => null,
    };
}
