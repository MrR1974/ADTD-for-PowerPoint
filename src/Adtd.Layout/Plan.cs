namespace Adtd.Layout;

/// <summary>
/// The slides to write, fully laid out in slide space (EMU, Y down). The layout engine produces it; the app's preview
/// and the PowerPoint writer both draw from it, so what's previewed is what's written. Nothing in it is PowerPoint XML:
/// colours are theme references and fonts are theme roles, resolved by whichever renderer draws it.
/// </summary>
public sealed record DeckPlan(IReadOnlyList<SlidePlan> Slides)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public enum SlideKind { Content, SectionHeader }

public sealed record SlidePlan(string Title, IReadOnlyList<PlanItem> Items)
{
    /// <summary>A section header goes on the template's section layout, with <see cref="Subtitle"/> in its text placeholder.</summary>
    public SlideKind Kind { get; init; } = SlideKind.Content;
    public string? Subtitle { get; init; }

    /// <summary>The PowerPoint section the slide belongs to; consecutive slides with the same name share one.</summary>
    public string? Section { get; init; }

    /// <summary>Speaker notes, if any.</summary>
    public string? Notes { get; init; }
}

public static class Emu
{
    public const double PerInch = 914400;
    public const double PerPoint = 12700;
    public static double Inches(double inches) => inches * PerInch;
    public static double Points(double points) => points * PerPoint;
}

/// <summary>A rectangle in slide EMU.</summary>
public readonly record struct Rect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;

    public Rect Inset(double d) => new(X + d, Y + d, Math.Max(0, W - 2 * d), Math.Max(0, H - 2 * d));
    public Rect Inset(double dx, double dy) => new(X + dx, Y + dy, Math.Max(0, W - 2 * dx), Math.Max(0, H - 2 * dy));
    public Rect Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
    public bool Intersects(Rect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public bool Contains(Rect o, double tolerance = 1) =>
        o.X >= X - tolerance && o.Y >= Y - tolerance && o.Right <= Right + tolerance && o.Bottom <= Bottom + tolerance;

    public static Rect Union(Rect a, Rect b)
    {
        double x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
        return new Rect(x0, y0, Math.Max(a.Right, b.Right) - x0, Math.Max(a.Bottom, b.Bottom) - y0);
    }

    public override string ToString() =>
        FormattableString.Invariant($"[{X / Emu.PerInch:0.00}, {Y / Emu.PerInch:0.00}, {W / Emu.PerInch:0.00} × {H / Emu.PerInch:0.00} in]");
}

public readonly record struct Point(double X, double Y);

// ---- colours ---------------------------------------------------------------------------------------

/// <summary>Theme colour slots as slides refer to them (tx/bg go through the master's colour map).</summary>
public enum SchemeSlot { Tx1, Bg1, Tx2, Bg2, Accent1, Accent2, Accent3, Accent4, Accent5, Accent6, Hlink }

public abstract record ColorRef;

/// <summary>A theme colour, optionally darkened (LumMod &lt; 100000) or lightened (LumOff &gt; 0), in DrawingML's 1/1000 %.</summary>
public sealed record ThemeColor(SchemeSlot Slot, int LumMod = 100000, int LumOff = 0, int Alpha = 100000) : ColorRef
{
    public static ThemeColor Accent(int n) => new(SchemeSlot.Accent1 + ((n - 1) % 6 + 6) % 6);
    public ThemeColor Darker(double fraction) => this with { LumMod = (int)Math.Round(LumMod * (1 - fraction)) };
    /// <summary>Mixed towards white: lumMod (1-f), lumOff f.</summary>
    public ThemeColor Lighter(double fraction) => this with { LumMod = (int)Math.Round(100000 * (1 - fraction)), LumOff = (int)Math.Round(100000 * fraction) };
    public ThemeColor WithAlpha(double alpha) => this with { Alpha = (int)Math.Round(alpha * 100000) };
}

/// <summary>A literal colour, "RRGGBB". Only for things that must not follow the theme.</summary>
public sealed record RgbColor(string Hex) : ColorRef;

public enum Dash { Solid, Dash, Dot, LongDash }

public sealed record Stroke(ColorRef Color, double WidthPt, Dash Dash = Dash.Solid);

// ---- text ------------------------------------------------------------------------------------------

public enum FontRole { Minor, Major }
public enum HAlign { Left, Center, Right }
public enum VAnchor { Top, Middle, Bottom }

public sealed record TextRun(string Text)
{
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    /// <summary>Overrides the text's size.</summary>
    public double? SizePt { get; init; }
    /// <summary>Overrides the text's colour.</summary>
    public ColorRef? Color { get; init; }
}

public sealed record TextPara(IReadOnlyList<TextRun> Runs, HAlign Align = HAlign.Left);

public sealed record PlanText(IReadOnlyList<TextPara> Paras, double SizePt, ColorRef Color)
{
    public FontRole Font { get; init; } = FontRole.Minor;
    public VAnchor Anchor { get; init; } = VAnchor.Middle;
    /// <summary>Inner margin on every side, EMU.</summary>
    public double Inset { get; init; } = Emu.Points(3.6);
    public bool Wrap { get; init; } = true;

    /// <summary>One paragraph of one run.</summary>
    public static PlanText Simple(string text, double sizePt, ColorRef color, bool bold = false, HAlign align = HAlign.Left) =>
        new([new TextPara([new TextRun(text) { Bold = bold }], align)], sizePt, color);

    /// <summary>One paragraph per line; the first line can be bold.</summary>
    public static PlanText Lines(IEnumerable<string> lines, double sizePt, ColorRef color, bool boldFirst = false, HAlign align = HAlign.Left) =>
        new(lines.Select((l, i) => new TextPara([new TextRun(l) { Bold = boldFirst && i == 0 }], align)).ToList(), sizePt, color);
}

// ---- items -----------------------------------------------------------------------------------------

public enum Geometry { Rect, RoundRect, Ellipse }

public enum ArrowEnds { None, End, Start, Both }

/// <summary>Every item has a readable name: it's what PowerPoint shows in the Selection Pane.</summary>
public abstract record PlanItem(string Name);

/// <summary>
/// A shape. Give it a <see cref="Key"/> to let links glue to it. <see cref="CornerRatio"/> is a rounded
/// rectangle's corner radius as a fraction of its shorter side (PowerPoint's default is 1/6).
/// </summary>
public sealed record BoxItem(string Name, Rect Bounds, Geometry Geometry) : PlanItem(Name)
{
    public string? Key { get; init; }
    public ColorRef? Fill { get; init; }
    public Stroke? Line { get; init; }
    public PlanText? Text { get; init; }
    public double CornerRatio { get; init; } = 1.0 / 6;
}

/// <summary>The domain controller icon.</summary>
public sealed record IconItem(string Name, Rect Bounds) : PlanItem(Name);

/// <summary>A text box: text with no fill or outline.</summary>
public sealed record TextItem(string Name, Rect Bounds, PlanText Text) : PlanItem(Name);

/// <summary>Items that move together. Its bounds are its children's.</summary>
public sealed record GroupItem(string Name, IReadOnlyList<PlanItem> Children) : PlanItem(Name);

/// <summary>
/// A connector glued at each end to the box with that key. <see cref="Route"/> runs from the start box to the end
/// box: two points for a straight line, or up to six alternating horizontal and vertical points for an elbow. Each end
/// should sit on one of its box's connection sites (<see cref="Sites"/>); the writer glues to the nearest site.
/// </summary>
public sealed record LinkItem(string Name, string FromKey, string ToKey, IReadOnlyList<Point> Route, Stroke Line) : PlanItem(Name)
{
    public ArrowEnds Arrows { get; init; } = ArrowEnds.None;
}

/// <summary>A native table. Column widths are proportional to <see cref="ColumnWeights"/> (equal if null).</summary>
public sealed record TableItem(string Name, Rect Bounds, IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows, double FontPt) : PlanItem(Name)
{
    public IReadOnlyList<double>? ColumnWeights { get; init; }
    /// <summary>Row height, EMU; the writer's default suits <see cref="FontPt"/>.</summary>
    public double? RowHeight { get; init; }
}

/// <summary>Where connectors can attach to a shape, in PowerPoint's order for each preset.</summary>
public static class Sites
{
    /// <summary>
    /// Rectangles and rounded rectangles: top, left, bottom, right. Ellipses: top, then anticlockwise in 45° steps
    /// (top-left, left, bottom-left, bottom, bottom-right, right, top-right).
    /// </summary>
    public static IReadOnlyList<Point> For(Geometry g, Rect r)
    {
        if (g != Geometry.Ellipse)
            return [new(r.CenterX, r.Y), new(r.X, r.CenterY), new(r.CenterX, r.Bottom), new(r.Right, r.CenterY)];
        double dx = r.W / 2 * Math.Cos(Math.PI / 4), dy = r.H / 2 * Math.Sin(Math.PI / 4);
        return
        [
            new(r.CenterX, r.Y), new(r.CenterX - dx, r.CenterY - dy), new(r.X, r.CenterY), new(r.CenterX - dx, r.CenterY + dy),
            new(r.CenterX, r.Bottom), new(r.CenterX + dx, r.CenterY + dy), new(r.Right, r.CenterY), new(r.CenterX + dx, r.CenterY - dy),
        ];
    }

    public static int Nearest(Geometry g, Rect r, Point p)
    {
        var sites = For(g, r);
        return Enumerable.Range(0, sites.Count).MinBy(i => Math.Pow(sites[i].X - p.X, 2) + Math.Pow(sites[i].Y - p.Y, 2));
    }
}
