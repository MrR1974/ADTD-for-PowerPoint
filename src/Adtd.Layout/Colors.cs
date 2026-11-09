namespace Adtd.Layout;

/// <summary>
/// Gives each domain a colour from the template's accents, the same on every slide, so colour works as a legend.
/// Accents are taken in an order that keeps neighbours distinct: Accent 1 first, then whichever order keeps every
/// colour as unlike the one before it as possible. After six domains the cycle repeats, lightened (dark accents) or darkened (light ones).
/// </summary>
public sealed class ColorAssigner
{
    readonly Dictionary<string, ThemeColor> _byDomain = new(StringComparer.OrdinalIgnoreCase);
    readonly ThemeColor _unknown;

    public IReadOnlyList<SchemeSlot> Order { get; }
    public IReadOnlyList<string> Warnings { get; }

    public ColorAssigner(IEnumerable<string> domains, ThemeInfo theme)
    {
        var (order, warnings) = SlotOrder(theme);
        Order = order;
        Warnings = warnings;
        var i = 0;
        foreach (var d in domains)
            if (!_byDomain.ContainsKey(d)) _byDomain[d] = Pick(i++, order, theme);
        _unknown = ColorMath.Luminance(theme.Resolve(new ThemeColor(SchemeSlot.Tx1))) < 0.5
            ? new ThemeColor(SchemeSlot.Tx1).Lighter(0.5) : new ThemeColor(SchemeSlot.Tx1).Darker(0.5);
    }

    /// <summary>The domain's colour; a neutral grey for a DC whose domain isn't known.</summary>
    public ThemeColor Of(string? domain) => domain != null && _byDomain.TryGetValue(domain, out var c) ? c : _unknown;

    /// <summary>The <paramref name="index"/>th colour of the cycle.</summary>
    public static ThemeColor Pick(int index, IReadOnlyList<SchemeSlot> order, ThemeInfo theme)
    {
        var slot = order[index % order.Count];
        var cycle = index / order.Count;
        var c = new ThemeColor(slot);
        if (cycle == 0) return c;
        var light = ColorMath.Luminance(theme.Resolve(c)) > 0.35;
        var step = (cycle - 1) % 3;   // three variants, then repeat
        return light ? c.Darker(new[] { 0.25, 0.5, 0.15 }[step]) : c.Lighter(new[] { 0.4, 0.6, 0.25 }[step]);
    }

    /// <summary>
    /// The accents in use order. Accents that barely show on the background are left to the end; a warning is given
    /// when the accents are all alike.
    /// </summary>
    public static (IReadOnlyList<SchemeSlot> Order, IReadOnlyList<string> Warnings) SlotOrder(ThemeInfo theme)
    {
        var bg = theme.Resolve(new ThemeColor(SchemeSlot.Bg1));
        var all = Enumerable.Range(0, 6).Select(i => SchemeSlot.Accent1 + i).ToList();
        var lab = all.ToDictionary(s => s, s => Lab(theme.Resolve(new ThemeColor(s))));
        var visible = all.Where(s => ColorMath.Contrast(theme.Resolve(new ThemeColor(s)), bg) >= 1.3).ToList();
        if (visible.Count < 2) visible = all;

        // Accent 1 first, then the order (of at most 5! = 120) whose closest pair of neighbours is furthest apart,
        // ties going to the larger total distance.
        var order = new List<SchemeSlot> { visible[0] };
        var best = (Min: -1.0, Sum: -1.0);
        List<SchemeSlot> bestRest = [];
        foreach (var perm in Permutations(visible.Skip(1).ToList()))
        {
            var seq = perm.Prepend(visible[0]).ToList();
            var steps = seq.Zip(seq.Skip(1), (a, b) => Distance(lab[a], lab[b])).ToList();
            var score = (Min: steps.DefaultIfEmpty(0).Min(), Sum: steps.Sum());
            if (score.Min > best.Min + 1e-9 || (Math.Abs(score.Min - best.Min) < 1e-9 && score.Sum > best.Sum + 1e-9)) { best = score; bestRest = perm; }
        }
        order.AddRange(bestRest);
        order.AddRange(all.Except(order));

        var warnings = new List<string>();
        var spread = visible.SelectMany(a => visible, (a, b) => Distance(lab[a], lab[b])).Max();
        if (spread < 20)
            warnings.Add($"The template's accent colours ({theme.Name}) are all alike, so domains are hard to tell apart by colour; their names are on every box.");
        return (order, warnings);
    }

    static IEnumerable<List<SchemeSlot>> Permutations(List<SchemeSlot> items)
    {
        if (items.Count <= 1) { yield return items; yield break; }
        for (var i = 0; i < items.Count; i++)
            foreach (var rest in Permutations(items.Where((_, j) => j != i).ToList()))
                yield return rest.Prepend(items[i]).ToList();
    }

    static double Distance((double L, double A, double B) x, (double L, double A, double B) y) =>
        Math.Sqrt(Math.Pow(x.L - y.L, 2) + Math.Pow(x.A - y.A, 2) + Math.Pow(x.B - y.B, 2));

    /// <summary>CIELAB (D65), for perceptual distance.</summary>
    static (double L, double A, double B) Lab(string hex)
    {
        var (r, g, b) = ColorMath.Rgb(hex);
        static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        double lr = Lin(r), lg = Lin(g), lb = Lin(b);
        var x = (0.4124 * lr + 0.3576 * lg + 0.1805 * lb) / 0.95047;
        var y = 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
        var z = (0.0193 * lr + 0.1192 * lg + 0.9505 * lb) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }
}
