using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Adtd.Layout;
using Adtd.PowerPoint;
using LRect = Adtd.Layout.Rect;
using Rect = System.Windows.Rect;

namespace Adtd.App.Preview;

/// <summary>
/// A layout as a wireframe: the slide in the theme's background colour, each placeholder outlined and named, and the
/// content area (where the diagrams go) filled, so it's clear which part of the slide the diagrams get.
/// </summary>
public static class Wireframe
{
    const double DipPerEmu = 96.0 / 914400;

    public static DrawingImage Render(TemplateInfo t, LayoutInfo layout)
    {
        var theme = t.ThemeOf(layout);
        var group = new DrawingGroup();
        var slide = new Rect(0, 0, t.SlideWidth * DipPerEmu, t.SlideHeight * DipPerEmu);
        var scale = slide.Width / 960;   // keeps labels and lines readable at any slide size
        using (var dc = group.Open())
        {
            dc.DrawRectangle(PlanRenderer.Brush(theme, new ThemeColor(SchemeSlot.Bg1)), null, slide);
            var ink = PlanRenderer.Brush(theme, new ThemeColor(SchemeSlot.Tx1, Alpha: 60000));
            var outline = new Pen(ink, 1.5 * scale) { DashStyle = new DashStyle([4, 3], 0) };
            outline.Freeze();
            foreach (var (kind, b) in layout.Placeholders.Where(p => p.Kind is not ("dt" or "ftr" or "sldNum")))
            {
                var r = R(b);
                dc.DrawRectangle(null, outline, r);
                Label(dc, Strings.Get(kind == "title" ? "Wire_Title" : "Wire_Placeholder") + (kind is "title" or "body" ? "" : $" ({kind})"), r, ink, 11 * scale, top: true);
            }
            var content = R(layout.Content);
            var accent = new ThemeColor(SchemeSlot.Accent1);
            var pen = new Pen(PlanRenderer.Brush(theme, accent), 2.5 * scale);
            pen.Freeze();
            dc.DrawRectangle(PlanRenderer.Brush(theme, accent.WithAlpha(0.18)), pen, content);
            Label(dc, Strings.Get("Wire_Content"), content, PlanRenderer.Brush(theme, theme.TextOn(new ThemeColor(SchemeSlot.Bg1))), 16 * scale, top: false);
        }
        group.ClipGeometry = new RectangleGeometry(slide);
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    static Rect R(LRect r) => new(r.X * DipPerEmu, r.Y * DipPerEmu, Math.Max(0, r.W * DipPerEmu), Math.Max(0, r.H * DipPerEmu));

    static void Label(DrawingContext dc, string text, Rect r, Brush brush, double size, bool top)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0)
        {
            MaxTextWidth = Math.Max(1, r.Width - 8),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var at = top ? new System.Windows.Point(r.X + 4, r.Y + 2) : new System.Windows.Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2);
        dc.DrawText(ft, at);
    }
}
