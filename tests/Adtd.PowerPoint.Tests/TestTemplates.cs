using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace Adtd.PowerPoint.Tests;

/// <summary>
/// Small templates built in code, each exercising one thing the template reader must handle. EMU throughout;
/// slides are 12192000 × 6858000 (13.333 × 7.5 in).
/// </summary>
internal static class TestTemplates
{
    const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\"";
    public const long W = 12192000, H = 6858000;

    public static readonly Ph MasterTitle = new("title", 0, 838200, 365125, 10515600, 1325563);
    public static readonly Ph MasterBody = new("body", 1, 838200, 1825625, 10515600, 4351338);

    public sealed record Ph(string? Type, uint Idx, long X, long Y, long Cx, long Cy, bool Positioned = true);
    public sealed record Layout(string Name, string Type, params Ph[] Placeholders);

    /// <summary>
    /// A .potx with a master (title at top, body below), four layouts and, if asked, existing slides in two sections and
    /// a custom show:
    /// "Title Slide" (centred title), "Title and Content" (positions inherited from the master), "Title Only",
    /// "Diagram" (own title; a hairline body that must be ignored), "Blank" (no placeholders).
    /// </summary>
    public static byte[] Basic(bool withSlides = false, bool dark = false, bool template = true) => Build(
        [
            new Layout("Title Slide", "title", new Ph("ctrTitle", 0, 1524000, 1122363, 9144000, 2387600), new Ph("subTitle", 1, 1524000, 3602038, 9144000, 1655762)),
            new Layout("Title and Content", "obj", new Ph("title", 0, 0, 0, 0, 0, false), new Ph(null, 1, 0, 0, 0, 0, false)),
            new Layout("Title Only", "titleOnly", new Ph("title", 0, 0, 0, 0, 0, false)),
            new Layout("Diagram", "cust", new Ph("title", 0, 457200, 228600, 6000000, 600000), new Ph("body", 13, 457200, 900000, 11277600, 50000)),
            new Layout("Blank", "blank"),
            new Layout("Section Header", "secHead", new Ph("title", 0, 831850, 1709738, 10515600, 2852737), new Ph("body", 1, 831850, 4589463, 10515600, 1500187)),
        ], MasterTitle, withSlides, dark, template);

    /// <summary>A master whose title is a tall strip down the right-hand side, as in Office's Pitchbook.</summary>
    public static byte[] VerticalTitle() => Build(
        [new Layout("Heading Only", "cust", new Ph("title", 0, 0, 0, 0, 0, false))],
        new Ph("title", 0, 11277600, 400000, 600000, 6000000), false, false, true,
        masterBody: new Ph("body", 1, 400000, 400000, 11277600, 6000000));

    static byte[] Build(Layout[] layouts, Ph masterTitle, bool withSlides, bool dark, bool template, Ph? masterBody = null)
    {
        var ms = new MemoryStream();
        using (var doc = PresentationDocument.Create(ms, template ? PresentationDocumentType.Template : PresentationDocumentType.Presentation))
        {
            var pp = doc.AddPresentationPart();
            var master = pp.AddNewPart<SlideMasterPart>("rId1");
            var theme = pp.AddNewPart<ThemePart>("rId2");
            master.AddPart(theme, "rId99");
            Feed(theme, Theme);
            var layoutParts = new List<SlideLayoutPart>();
            var ids = new StringBuilder();
            for (var i = 0; i < layouts.Length; i++)
            {
                var lp = master.AddNewPart<SlideLayoutPart>($"rId{i + 1}");
                lp.AddPart(master, "rId1");
                Feed(lp, $"<p:sldLayout {Ns} type=\"{layouts[i].Type}\" preserve=\"1\"><p:cSld name=\"{layouts[i].Name}\">{Tree(layouts[i].Placeholders)}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
                layoutParts.Add(lp);
                ids.Append($"<p:sldLayoutId id=\"{2147483649L + i}\" r:id=\"rId{i + 1}\"/>");
            }
            var map = dark
                ? "bg1=\"dk1\" tx1=\"lt1\" bg2=\"dk2\" tx2=\"lt2\""
                : "bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\"";
            Feed(master, $"<p:sldMaster {Ns}><p:cSld>{Tree([masterTitle, masterBody ?? MasterBody, new Ph("sldNum", 12, 8610600, 6356350, 2743200, 365125)])}</p:cSld>" +
                         $"<p:clrMap {map} accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                         $"<p:sldLayoutIdLst>{ids}</p:sldLayoutIdLst></p:sldMaster>");

            var slides = new StringBuilder();
            var ext = "";
            if (withSlides)
            {
                for (var i = 0; i < 2; i++)
                {
                    var sp = pp.AddNewPart<SlidePart>($"rIdS{i}");
                    sp.AddPart(layoutParts[1], "rId1");
                    Feed(sp, $"<p:sld {Ns}><p:cSld>{Tree([])}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
                    slides.Append($"<p:sldId id=\"{256 + i}\" r:id=\"rIdS{i}\"/>");
                }
                ext = "<p:extLst><p:ext uri=\"{521415D9-36F7-43E2-AB2F-B90AF26B5E84}\"><p14:sectionLst>" +
                      "<p14:section name=\"Intro\" id=\"{A0000000-0000-0000-0000-000000000001}\"><p14:sldIdLst><p14:sldId id=\"256\"/></p14:sldIdLst></p14:section>" +
                      "<p14:section name=\"Body\" id=\"{A0000000-0000-0000-0000-000000000002}\"><p14:sldIdLst><p14:sldId id=\"257\"/></p14:sldIdLst></p14:section>" +
                      "</p14:sectionLst></p:ext></p:extLst>";
            }
            var custShow = withSlides ? "<p:custShowLst><p:custShow name=\"Short\" id=\"0\"><p:sldLst><p:sld r:id=\"rIdS0\"/></p:sldLst></p:custShow></p:custShowLst>" : "";
            Feed(pp, $"<p:presentation {Ns}><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>" +
                     (withSlides ? $"<p:sldIdLst>{slides}</p:sldIdLst>" : "") +
                     $"<p:sldSz cx=\"{W}\" cy=\"{H}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>{custShow}{ext}</p:presentation>");
        }
        return ms.ToArray();
    }

    static string Tree(IEnumerable<Ph> phs)
    {
        var o = new StringBuilder("<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>");
        var id = 2;
        foreach (var p in phs)
        {
            var type = p.Type == null ? "" : $" type=\"{p.Type}\"";
            var xfrm = p.Positioned ? $"<a:xfrm><a:off x=\"{p.X}\" y=\"{p.Y}\"/><a:ext cx=\"{p.Cx}\" cy=\"{p.Cy}\"/></a:xfrm>" : "";
            o.Append($"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"Placeholder {id}\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph{type} idx=\"{p.Idx}\"/></p:nvPr></p:nvSpPr>" +
                     $"<p:spPr>{xfrm}</p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>");
            id++;
        }
        return o.Append("</p:spTree>").ToString();
    }

    static string Clr(string n, string v) => $"<a:{n}><a:srgbClr val=\"{v}\"/></a:{n}>";
    const string Fill3 = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>";
    const string Ln3 = "<a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>";

    public static readonly string Theme =
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Test Theme\"><a:themeElements>" +
        "<a:clrScheme name=\"Test\">" + Clr("dk1", "111111") + Clr("lt1", "FAFAFA") + Clr("dk2", "222244") + Clr("lt2", "EEEEDD") +
        Clr("accent1", "4472C4") + Clr("accent2", "ED7D31") + Clr("accent3", "A5A5A5") + Clr("accent4", "FFC000") +
        Clr("accent5", "5B9BD5") + Clr("accent6", "70AD47") + Clr("hlink", "0563C1") + Clr("folHlink", "954F72") + "</a:clrScheme>" +
        "<a:fontScheme name=\"Test\"><a:majorFont><a:latin typeface=\"Georgia\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Verdana\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"Test\"><a:fillStyleLst>" + Fill3 + "</a:fillStyleLst><a:lnStyleLst>" + Ln3 + "</a:lnStyleLst>" +
        "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
        "<a:bgFillStyleLst>" + Fill3 + "</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>";

    static void Feed(OpenXmlPart part, string xml)
    {
        using var s = new MemoryStream(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml));
        part.FeedData(s);
    }
}
