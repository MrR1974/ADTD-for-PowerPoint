// Shape, text and picture XML adapted from Recast's src/Recast.PowerPoint/SlideBuilder.cs,
// reworked for theme colours (schemeClr), theme fonts (+mj-lt/+mn-lt) and slide-space plans.
using System.Globalization;
using System.Security;
using System.Text;
using Adtd.Layout;
using static Adtd.PowerPoint.Xml;

namespace Adtd.PowerPoint;

internal static class Xml
{
    public const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                             "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                             "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";
    public static string N(double v) => ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
    public static string X(string s) => SecurityElement.Escape(s) ?? "";
    public static string Xfrm(Rect r, string element = "a:xfrm") =>
        $"<{element}><a:off x=\"{N(r.X)}\" y=\"{N(r.Y)}\"/><a:ext cx=\"{N(Math.Max(0, r.W))}\" cy=\"{N(Math.Max(0, r.H))}\"/></{element}>";
}

/// <summary>Writes one slide's shape tree from a <see cref="SlidePlan"/>.</summary>
internal sealed class SlideWriter(string iconSvgRel, string iconPngRel, bool hasSvg)
{
    readonly Dictionary<PlanItem, int> _ids = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<string, (int Id, BoxItem Box)> _targets = new(StringComparer.Ordinal);
    public List<string> Warnings { get; } = [];
    public bool UsesIcon { get; private set; }

    /// <summary>The spTree contents after the title (shape ids from 3; the title placeholder is 2).</summary>
    public string Write(SlidePlan slide)
    {
        var next = 3;
        void Number(PlanItem item)
        {
            _ids[item] = next++;
            if (item is GroupItem g) foreach (var c in g.Children) Number(c);
            if (item is BoxItem { Key: { } key } b && !_targets.TryAdd(key, (_ids[item], b)))
                Warnings.Add($"\"{slide.Title}\": two shapes share the key {key}; links glue to the first.");
        }
        foreach (var item in slide.Items) Number(item);

        var o = new StringBuilder();
        foreach (var item in slide.Items) Item(item, o);
        return o.ToString();
    }

    void Item(PlanItem item, StringBuilder o)
    {
        switch (item)
        {
            case BoxItem b: Box(b, o); break;
            case TextItem t: Text(t, o); break;
            case IconItem i: Icon(i, o); break;
            case GroupItem g: Group(g, o); break;
            case LinkItem l: Link(l, o); break;
            case TableItem t: Table(t, o); break;
        }
    }

    string Nv(PlanItem item) => $"<p:cNvPr id=\"{_ids[item]}\" name=\"{X(item.Name)}\"/>";

    void Box(BoxItem b, StringBuilder o)
    {
        var prst = b.Geometry switch { Geometry.RoundRect => "roundRect", Geometry.Ellipse => "ellipse", _ => "rect" };
        var av = b.Geometry == Geometry.RoundRect ? $"<a:gd name=\"adj\" fmla=\"val {N(Math.Clamp(b.CornerRatio, 0, 0.5) * 100000)}\"/>" : "";
        o.Append($"<p:sp><p:nvSpPr>{Nv(b)}<p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>{Xfrm(b.Bounds)}<a:prstGeom prst=\"{prst}\"><a:avLst>{av}</a:avLst></a:prstGeom>");
        o.Append(b.Fill is { } f ? $"<a:solidFill>{Color(f)}</a:solidFill>" : "<a:noFill/>");
        o.Append(Line(b.Line, ArrowEnds.None));
        o.Append("</p:spPr>");
        o.Append(b.Text is { } t ? TextBody(t) : "<p:txBody><a:bodyPr rtlCol=\"0\" anchor=\"ctr\"/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\" dirty=\"0\"/></a:p></p:txBody>");
        o.Append("</p:sp>");
    }

    void Text(TextItem t, StringBuilder o)
    {
        o.Append($"<p:sp><p:nvSpPr>{Nv(t)}<p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr>{Xfrm(t.Bounds)}<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>");
        o.Append(TextBody(t.Text)).Append("</p:sp>");
    }

    void Icon(IconItem i, StringBuilder o)
    {
        UsesIcon = true;
        var svg = hasSvg
            ? $"<a:extLst><a:ext uri=\"{{96DAC541-7B7A-43D3-8B79-37D633B846F1}}\"><asvg:svgBlip xmlns:asvg=\"http://schemas.microsoft.com/office/drawing/2016/SVG/main\" r:embed=\"{iconSvgRel}\"/></a:ext></a:extLst>"
            : "";
        o.Append($"<p:pic><p:nvPicPr>{Nv(i)}<p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                 $"<p:blipFill><a:blip r:embed=\"{iconPngRel}\">{svg}</a:blip><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                 $"<p:spPr>{Xfrm(i.Bounds)}<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>");
    }

    void Group(GroupItem g, StringBuilder o)
    {
        var bounds = Bounds(g);
        if (bounds is not Rect r) return;
        // Child offset/extent equal to the group's own, so children keep slide coordinates.
        o.Append($"<p:grpSp><p:nvGrpSpPr>{Nv(g)}<p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr>" +
                 $"<a:xfrm><a:off x=\"{N(r.X)}\" y=\"{N(r.Y)}\"/><a:ext cx=\"{N(r.W)}\" cy=\"{N(r.H)}\"/><a:chOff x=\"{N(r.X)}\" y=\"{N(r.Y)}\"/><a:chExt cx=\"{N(r.W)}\" cy=\"{N(r.H)}\"/></a:xfrm></p:grpSpPr>");
        foreach (var c in g.Children) Item(c, o);
        o.Append("</p:grpSp>");
    }

    internal static Rect? Bounds(PlanItem item) => item switch
    {
        BoxItem b => b.Bounds,
        TextItem t => t.Bounds,
        IconItem i => i.Bounds,
        TableItem t => t.Bounds,
        LinkItem l => l.Route.Count == 0 ? null : new Rect(l.Route.Min(p => p.X), l.Route.Min(p => p.Y),
                                                           l.Route.Max(p => p.X) - l.Route.Min(p => p.X), l.Route.Max(p => p.Y) - l.Route.Min(p => p.Y)),
        GroupItem g => g.Children.Select(Bounds).OfType<Rect>().Aggregate((Rect?)null, (acc, r) => acc is Rect a ? Rect.Union(a, r) : r),
        _ => null,
    };

    void Link(LinkItem l, StringBuilder o)
    {
        if (l.Route.Count < 2) { Warnings.Add($"Link {l.Name} has no route; left out."); return; }
        var route = Routes.Match(l.Route);
        if (route is not { } r)
        {
            Warnings.Add($"Link {l.Name}: route isn't a straight line or a 2–5 segment elbow; drawn straight.");
            r = Routes.Match([l.Route[0], l.Route[^1]])!.Value;
        }
        string Glue(string element, string key, Point end)
        {
            if (!_targets.TryGetValue(key, out var t)) { Warnings.Add($"Link {l.Name}: nothing on the slide has key {key}; that end isn't glued."); return ""; }
            return $"<a:{element} id=\"{t.Id}\" idx=\"{Sites.Nearest(t.Box.Geometry, t.Box.Bounds, end)}\"/>";
        }
        o.Append($"<p:cxnSp><p:nvCxnSpPr>{Nv(l)}<p:cNvCxnSpPr>{Glue("stCxn", l.FromKey, l.Route[0])}{Glue("endCxn", l.ToKey, l.Route[^1])}</p:cNvCxnSpPr><p:nvPr/></p:nvCxnSpPr>");
        o.Append($"<p:spPr>{r.Xfrm}{r.Geometry}<a:noFill/>{Line(l.Line, l.Arrows)}</p:spPr></p:cxnSp>");
    }

    void Table(TableItem t, StringBuilder o)
    {
        var cols = Math.Max(t.Header.Count, t.Rows.Count == 0 ? 0 : t.Rows.Max(r => r.Count));
        if (cols == 0) return;
        var weights = t.ColumnWeights is { Count: > 0 } w && w.Count == cols ? w : Enumerable.Repeat(1.0, cols).ToList();
        var total = weights.Sum();
        var rowH = t.RowHeight ?? Emu.Points(t.FontPt * 2);
        var rows = t.Rows.Count + (t.Header.Count > 0 ? 1 : 0);
        o.Append($"<p:graphicFrame><p:nvGraphicFramePr>{Nv(t)}<p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>");
        o.Append(Xfrm(t.Bounds with { H = rowH * rows }, "p:xfrm"));
        o.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl>");
        o.Append($"<a:tblPr firstRow=\"{(t.Header.Count > 0 ? 1 : 0)}\" bandRow=\"1\"><a:tableStyleId>{{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}}</a:tableStyleId></a:tblPr><a:tblGrid>");
        // Column widths must add up to the frame's width exactly.
        long used = 0;
        for (var i = 0; i < cols; i++)
        {
            var cw = i == cols - 1 ? (long)Math.Round(t.Bounds.W) - used : (long)Math.Round(t.Bounds.W * weights[i] / total);
            used += cw;
            o.Append($"<a:gridCol w=\"{cw}\"/>");
        }
        o.Append("</a:tblGrid>");
        var ins = N(Emu.Points(Math.Max(2, t.FontPt * 0.4)));
        void Row(IReadOnlyList<string> cells, bool bold)
        {
            o.Append($"<a:tr h=\"{N(rowH)}\">");
            for (var i = 0; i < cols; i++)
            {
                var text = i < cells.Count ? cells[i] : "";
                o.Append($"<a:tc><a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\" sz=\"{N(t.FontPt * 100)}\"{(bold ? " b=\"1\"" : "")} dirty=\"0\"><a:latin typeface=\"+mn-lt\"/></a:rPr><a:t>{X(text)}</a:t></a:r></a:p></a:txBody>" +
                         $"<a:tcPr marL=\"{ins}\" marR=\"{ins}\" marT=\"{N(Emu.Points(1))}\" marB=\"{N(Emu.Points(1))}\" anchor=\"ctr\"/></a:tc>");
            }
            o.Append("</a:tr>");
        }
        if (t.Header.Count > 0) Row(t.Header, true);
        foreach (var r in t.Rows) Row(r, false);
        o.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
    }

    // ---- styling ---------------------------------------------------------------------------------

    internal static string Color(ColorRef c) => c switch
    {
        ThemeColor t => $"<a:schemeClr val=\"{Slot(t.Slot)}\">{(t.LumMod != 100000 ? $"<a:lumMod val=\"{t.LumMod}\"/>" : "")}" +
                        $"{(t.LumOff != 0 ? $"<a:lumOff val=\"{t.LumOff}\"/>" : "")}{(t.Alpha != 100000 ? $"<a:alpha val=\"{t.Alpha}\"/>" : "")}</a:schemeClr>",
        RgbColor r => $"<a:srgbClr val=\"{r.Hex.TrimStart('#').ToUpperInvariant()}\"/>",
        _ => "<a:schemeClr val=\"tx1\"/>",
    };

    static string Slot(SchemeSlot s) => s switch
    {
        SchemeSlot.Tx1 => "tx1", SchemeSlot.Bg1 => "bg1", SchemeSlot.Tx2 => "tx2", SchemeSlot.Bg2 => "bg2", SchemeSlot.Hlink => "hlink",
        _ => $"accent{s - SchemeSlot.Accent1 + 1}",
    };

    static string Line(Stroke? s, ArrowEnds arrows)
    {
        if (s == null) return "<a:ln><a:noFill/></a:ln>";
        var dash = s.Dash switch { Dash.Dash => "dash", Dash.Dot => "sysDot", Dash.LongDash => "lgDash", _ => "solid" };
        var head = arrows is ArrowEnds.Start or ArrowEnds.Both ? "<a:headEnd type=\"triangle\" w=\"med\" len=\"med\"/>" : "";
        var tail = arrows is ArrowEnds.End or ArrowEnds.Both ? "<a:tailEnd type=\"triangle\" w=\"med\" len=\"med\"/>" : "";
        return $"<a:ln w=\"{N(s.WidthPt * Emu.PerPoint)}\"><a:solidFill>{Color(s.Color)}</a:solidFill><a:prstDash val=\"{dash}\"/>{head}{tail}</a:ln>";
    }

    internal static string TextBody(PlanText t)
    {
        var anchor = t.Anchor switch { VAnchor.Top => "t", VAnchor.Bottom => "b", _ => "ctr" };
        var ins = N(t.Inset);
        var font = t.Font == FontRole.Major ? "+mj-lt" : "+mn-lt";
        var o = new StringBuilder($"<p:txBody><a:bodyPr wrap=\"{(t.Wrap ? "square" : "none")}\" lIns=\"{ins}\" tIns=\"{ins}\" rIns=\"{ins}\" bIns=\"{ins}\" anchor=\"{anchor}\" rtlCol=\"0\"><a:noAutofit/></a:bodyPr><a:lstStyle/>");
        foreach (var p in t.Paras)
        {
            var algn = p.Align switch { HAlign.Center => "ctr", HAlign.Right => "r", _ => "l" };
            o.Append($"<a:p><a:pPr algn=\"{algn}\" marL=\"0\" indent=\"0\"><a:spcBef><a:spcPts val=\"0\"/></a:spcBef><a:buNone/></a:pPr>");
            foreach (var r in p.Runs)
            {
                var size = r.SizePt ?? t.SizePt;
                o.Append($"<a:r><a:rPr lang=\"en-US\" sz=\"{N(size * 100)}\"{(r.Bold ? " b=\"1\"" : " b=\"0\"")}{(r.Italic ? " i=\"1\"" : "")} dirty=\"0\">" +
                         $"<a:solidFill>{Color(r.Color ?? t.Color)}</a:solidFill><a:latin typeface=\"{font}\"/><a:cs typeface=\"{font}\"/></a:rPr><a:t>{X(r.Text)}</a:t></a:r>");
            }
            o.Append($"<a:endParaRPr lang=\"en-US\" sz=\"{N(t.SizePt * 100)}\" dirty=\"0\"/></a:p>");
        }
        if (t.Paras.Count == 0) o.Append("<a:p><a:endParaRPr lang=\"en-US\" dirty=\"0\"/></a:p>");
        o.Append("</p:txBody>");
        return o.ToString();
    }
}
