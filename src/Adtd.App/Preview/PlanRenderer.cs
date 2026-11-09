// The approach (a frozen DrawingImage in DIPs, FormattedText for text) follows
// Recast's src/Recast.App/Preview/DiagramRenderer.cs; the drawing itself is new, for DeckPlan.
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Adtd.Layout;
using LPoint = Adtd.Layout.Point;
using LRect = Adtd.Layout.Rect;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Adtd.App.Preview;

/// <summary>
/// Draws a planned slide as a WPF drawing (1 unit = 1/96 in): the background colour, the title and every plan item, in
/// the theme's colours and fonts. The template's own artwork (logos, bars, footers) isn't drawn, and text is laid out
/// by WPF rather than PowerPoint, so it's a faithful guide rather than a pixel match.
/// </summary>
public static class PlanRenderer
{
    const double DipPerEmu = 96.0 / 914400;
    const double DipPerPt = 96.0 / 72;

    public sealed record Context(SlideFrame Frame, ThemeInfo Theme, byte[]? IconPng);

    /// <summary>The slide as a frozen drawing, safe to build on a background thread and show on the UI thread.</summary>
    public static DrawingImage Render(SlidePlan slide, Context c)
    {
        var group = new DrawingGroup();
        var bounds = new Rect(0, 0, c.Frame.Width * DipPerEmu, c.Frame.Height * DipPerEmu);
        var icon = c.IconPng is { } png ? Bitmap(png) : null;
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brush(c.Theme, new ThemeColor(SchemeSlot.Bg1)), null, bounds);
            if (slide.Kind == SlideKind.SectionHeader) SectionHeader(dc, slide, c);
            else Title(dc, slide.Title, c);
            foreach (var item in slide.Items) Draw(dc, item, c, icon);
        }
        group.ClipGeometry = new RectangleGeometry(bounds);
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    static Rect R(LRect r) => new(r.X * DipPerEmu, r.Y * DipPerEmu, Math.Max(0, r.W * DipPerEmu), Math.Max(0, r.H * DipPerEmu));
    static Point P(LPoint p) => new(p.X * DipPerEmu, p.Y * DipPerEmu);

    // ---- titles ------------------------------------------------------------------------------------

    static void Title(DrawingContext dc, string title, Context c)
    {
        // Where PptxComposer puts it: the layout's title placeholder, else a box above the content area.
        var box = c.Frame.Title ?? new LRect(c.Frame.Content.X, Math.Max(Emu.Inches(0.2), c.Frame.Content.Y - Emu.Inches(0.7)), c.Frame.Content.W, Emu.Inches(0.6));
        var text = PlanText.Simple(title, 28, new ThemeColor(SchemeSlot.Tx1)) with { Font = FontRole.Major, Wrap = false, Anchor = c.Frame.Title == null ? VAnchor.Bottom : VAnchor.Middle };
        Text(dc, Shrink(text, R(box), c), R(box), c);
    }

    static void SectionHeader(DrawingContext dc, SlidePlan slide, Context c)
    {
        var w = c.Frame.Width * DipPerEmu;
        var h = c.Frame.Height * DipPerEmu;
        var titleBox = new Rect(w * 0.08, h * 0.3, w * 0.84, h * 0.22);
        Text(dc, Shrink(PlanText.Simple(slide.Title, 40, new ThemeColor(SchemeSlot.Tx1)) with { Font = FontRole.Major, Wrap = false, Anchor = VAnchor.Bottom }, titleBox, c), titleBox, c);
        if (slide.Subtitle is { } sub)
        {
            var subBox = new Rect(w * 0.08, h * 0.54, w * 0.84, h * 0.14);
            Text(dc, PlanText.Simple(sub, 20, new ThemeColor(SchemeSlot.Tx2)) with { Anchor = VAnchor.Top }, subBox, c);
        }
    }

    /// <summary>Steps a one-line title down until it fits its box, as PowerPoint's autofit would.</summary>
    static PlanText Shrink(PlanText t, Rect box, Context c)
    {
        var size = t.SizePt;
        while (size > 10 && Width(t with { SizePt = size }, c) > box.Width - 2 * t.Inset * DipPerEmu) size -= 2;
        return t with { SizePt = size };
    }

    static double Width(PlanText t, Context c) => t.Paras.Select(p => Format(p, t, c, double.PositiveInfinity).WidthIncludingTrailingWhitespace).DefaultIfEmpty(0).Max();

    // ---- items -------------------------------------------------------------------------------------

    static void Draw(DrawingContext dc, PlanItem item, Context c, ImageSource? icon)
    {
        switch (item)
        {
            case GroupItem g:
                foreach (var child in g.Children) Draw(dc, child, c, icon);
                break;
            case BoxItem b:
                Box(dc, b, c);
                break;
            case TextItem t:
                Text(dc, t.Text, R(t.Bounds), c);
                break;
            case IconItem i:
                if (icon != null) dc.DrawImage(icon, R(i.Bounds));
                else dc.DrawRectangle(Brushes.Gainsboro, null, R(i.Bounds));
                break;
            case LinkItem l:
                Link(dc, l, c);
                break;
            case TableItem t:
                Table(dc, t, c);
                break;
        }
    }

    static void Box(DrawingContext dc, BoxItem b, Context c)
    {
        var r = R(b.Bounds);
        var fill = b.Fill is { } f ? Brush(c.Theme, f) : null;
        var pen = b.Line is { } l ? Pen(c.Theme, l) : null;
        switch (b.Geometry)
        {
            case Adtd.Layout.Geometry.Ellipse:
                dc.DrawEllipse(fill, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                break;
            case Adtd.Layout.Geometry.RoundRect:
                var rad = b.CornerRatio * Math.Min(r.Width, r.Height);
                dc.DrawRoundedRectangle(fill, pen, r, rad, rad);
                break;
            default:
                dc.DrawRectangle(fill, pen, r);
                break;
        }
        if (b.Text is { } t) Text(dc, t, r, c);
    }

    static void Link(DrawingContext dc, LinkItem l, Context c)
    {
        if (l.Route.Count < 2) return;
        var pen = Pen(c.Theme, l.Line);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(l.Route[0]), false, false);
            foreach (var p in l.Route.Skip(1)) ctx.LineTo(P(p), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
        var brush = Brush(c.Theme, l.Line.Color);
        if (l.Arrows is ArrowEnds.End or ArrowEnds.Both) Arrow(dc, P(l.Route[^2]), P(l.Route[^1]), l.Line.WidthPt, brush);
        if (l.Arrows is ArrowEnds.Start or ArrowEnds.Both) Arrow(dc, P(l.Route[1]), P(l.Route[0]), l.Line.WidthPt, brush);
    }

    /// <summary>A filled triangle at <paramref name="tip"/>, pointing away from <paramref name="from"/>: PowerPoint's medium arrowhead.</summary>
    static void Arrow(DrawingContext dc, Point from, Point tip, double widthPt, Brush brush)
    {
        var d = tip - from;
        if (d.Length < 0.01) return;
        d.Normalize();
        var n = new Vector(-d.Y, d.X);
        var len = Math.Max(6, widthPt * DipPerPt * 3);
        var back = tip - d * len;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(back + n * len / 2, false, false);
            ctx.LineTo(back - n * len / 2, false, false);
        }
        g.Freeze();
        dc.DrawGeometry(brush, null, g);
    }

    /// <summary>
    /// A native table as "Medium Style 2 – Accent 1" draws it: an accent-1 header with bold light text, rows banded in
    /// accent-1 tints, white rules between cells.
    /// </summary>
    static void Table(DrawingContext dc, TableItem t, Context c)
    {
        var cols = Math.Max(t.Header.Count, t.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
        if (cols == 0) return;
        var weights = t.ColumnWeights is { Count: > 0 } w && w.Count == cols ? w : Enumerable.Repeat(1.0, cols).ToList();
        var r = R(t.Bounds);
        var rowH = (t.RowHeight ?? Emu.Points(t.FontPt * 2)) * DipPerEmu;
        var xs = new double[cols + 1];
        xs[0] = r.X;
        for (var i = 0; i < cols; i++) xs[i + 1] = xs[i] + r.Width * weights[i] / weights.Sum();

        var accent = new ThemeColor(SchemeSlot.Accent1);
        var rule = new Pen(Brush(c.Theme, new ThemeColor(SchemeSlot.Bg1)), 1);
        rule.Freeze();
        var rows = new List<(IReadOnlyList<string> Cells, ColorRef Fill, bool Header)>();
        if (t.Header.Count > 0) rows.Add((t.Header, accent, true));
        rows.AddRange(t.Rows.Select((cells, i) => (cells, (ColorRef)accent.Lighter(i % 2 == 0 ? 0.6 : 0.8), false)));
        var y = r.Y;
        foreach (var (cells, fill, header) in rows)
        {
            var textColor = header ? c.Theme.TextOn(fill) : new ThemeColor(SchemeSlot.Tx1);
            for (var i = 0; i < cols; i++)
            {
                var cell = new Rect(xs[i], y, xs[i + 1] - xs[i], rowH);
                dc.DrawRectangle(Brush(c.Theme, fill), rule, cell);
                var text = i < cells.Count ? cells[i] : "";
                var pt = new PlanText([new TextPara([new TextRun(text) { Bold = header }])], t.FontPt, textColor) { Inset = Emu.Points(4), Wrap = false };
                Text(dc, pt, cell, c);
            }
            y += rowH;
        }
    }

    // ---- text --------------------------------------------------------------------------------------

    static void Text(DrawingContext dc, PlanText t, Rect box, Context c)
    {
        var inset = t.Inset * DipPerEmu;
        var inner = new Rect(box.X + inset, box.Y + inset, Math.Max(1, box.Width - 2 * inset), Math.Max(1, box.Height - 2 * inset));
        var laid = t.Paras.Select(p => Format(p, t, c, t.Wrap ? inner.Width : double.PositiveInfinity)).ToList();
        var total = laid.Sum(f => f.Height);
        var y = t.Anchor switch
        {
            VAnchor.Top => inner.Y,
            VAnchor.Bottom => inner.Bottom - total,
            _ => inner.Y + (inner.Height - total) / 2,
        };
        dc.PushClip(new RectangleGeometry(box));
        for (var i = 0; i < laid.Count; i++)
        {
            var ft = laid[i];
            var x = t.Paras[i].Align switch
            {
                HAlign.Center => inner.X + (inner.Width - ft.WidthIncludingTrailingWhitespace) / 2,
                HAlign.Right => inner.Right - ft.WidthIncludingTrailingWhitespace,
                _ => inner.X,
            };
            if (t.Wrap) x = inner.X;   // wrapped text is aligned by the FormattedText itself
            dc.DrawText(ft, new Point(x, y));
            y += ft.Height;
        }
        dc.Pop();
    }

    static FormattedText Format(TextPara p, PlanText t, Context c, double maxWidth)
    {
        var runs = p.Runs.Count > 0 ? p.Runs : [new TextRun(" ")];
        var text = string.Concat(runs.Select(r => r.Text));
        if (text.Length == 0) text = " ";
        var family = ThemeFonts.Family(t.Font == FontRole.Major ? c.Theme.MajorFont : c.Theme.MinorFont);
        var size = t.SizePt * DipPerPt;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                   new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brush(c.Theme, t.Color), 1.0)
        {
            LineHeight = size * 1.2,
            Trimming = TextTrimming.None,
        };
        if (!double.IsInfinity(maxWidth))
        {
            ft.MaxTextWidth = Math.Max(1, maxWidth);
            ft.TextAlignment = p.Align switch { HAlign.Center => TextAlignment.Center, HAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
        }
        var at = 0;
        foreach (var r in runs)
        {
            if (r.Text.Length == 0) continue;
            if (r.Bold) ft.SetFontWeight(FontWeights.Bold, at, r.Text.Length);
            if (r.Italic) ft.SetFontStyle(FontStyles.Italic, at, r.Text.Length);
            if (r.SizePt is { } s) ft.SetFontSize(s * DipPerPt, at, r.Text.Length);
            if (r.Color is { } col) ft.SetForegroundBrush(Brush(c.Theme, col), at, r.Text.Length);
            at += r.Text.Length;
        }
        return ft;
    }

    // ---- colours and images ------------------------------------------------------------------------

    public static Color ColorOf(ThemeInfo theme, ColorRef c)
    {
        var hex = theme.Resolve(c);
        var alpha = c is ThemeColor tc ? tc.Alpha / 100000.0 : 1;
        return Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255),
            byte.Parse(hex[..2], NumberStyles.HexNumber), byte.Parse(hex[2..4], NumberStyles.HexNumber), byte.Parse(hex[4..6], NumberStyles.HexNumber));
    }

    public static SolidColorBrush Brush(ThemeInfo theme, ColorRef c)
    {
        var b = new SolidColorBrush(ColorOf(theme, c));
        b.Freeze();
        return b;
    }

    static Pen Pen(ThemeInfo theme, Stroke s)
    {
        var pen = new Pen(Brush(theme, s.Color), Math.Max(0.5, s.WidthPt * DipPerPt)) { LineJoin = PenLineJoin.Round };
        pen.DashStyle = s.Dash switch
        {
            Dash.Dash => new DashStyle([4, 3], 0),
            Dash.Dot => new DashStyle([1, 2], 0),
            Dash.LongDash => new DashStyle([8, 3], 0),
            _ => DashStyles.Solid,
        };
        pen.Freeze();
        return pen;
    }

    public static BitmapSource? Bitmap(byte[] png)
    {
        try
        {
            var frame = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Theme fonts for WPF. Office's cloud fonts (Aptos and friends) aren't installed for Windows, only downloaded into
/// Office's font cache, so they're loaded from there when the system doesn't have them, as the text measurer does.
/// </summary>
public static class ThemeFonts
{
    static readonly Dictionary<string, FontFamily> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static FontFamily Family(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var f)) return f;
            f = Find(name);
            Cache[name] = f;
            return f;
        }
    }

    static FontFamily Find(string name)
    {
        if (Fonts.SystemFontFamilies.Any(f => f.Source.Equals(name, StringComparison.OrdinalIgnoreCase))) return new FontFamily(name);
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "FontCache", "4", "CloudFonts", name);
        if (Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
            return new FontFamily(new Uri(dir.TrimEnd('\\') + "\\"), $"./#{name}, Segoe UI");
        return new FontFamily($"{name}, Segoe UI");
    }
}
