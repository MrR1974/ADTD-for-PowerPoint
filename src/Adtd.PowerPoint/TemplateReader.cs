using System.Xml.Linq;
using Adtd.Layout;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace Adtd.PowerPoint;

/// <summary>What a template offers: its slide size, layouts and themes.</summary>
public sealed record TemplateInfo
{
    public required string Name { get; init; }
    public required double SlideWidth { get; init; }
    public required double SlideHeight { get; init; }
    public required IReadOnlyList<ThemeInfo> Themes { get; init; }   // one per master, by master index
    public required IReadOnlyList<LayoutInfo> Layouts { get; init; }
    public int ExistingSlides { get; init; }

    /// <summary>A layout by name (case-insensitive), optionally on a given master.</summary>
    public LayoutInfo? Find(string name, int? master = null) =>
        Layouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase) && (master == null || l.MasterIndex == master));

    /// <summary>
    /// The layout to use when the user hasn't picked one: the first "Title Only", else the first "Title and Content",
    /// else the simplest layout (fewest placeholders) with a title and a content area of at least 30% of the slide
    /// (title, section, cover and closing layouts aside), else the first layout.
    /// Corporate templates often use custom layouts only, so this is a fallback, not a rule.
    /// </summary>
    public LayoutInfo DefaultLayout =>
        Layouts.FirstOrDefault(l => l.Type == "titleOnly")
        ?? Layouts.FirstOrDefault(l => l.Type == "obj")
        ?? Layouts.Where(l => l.Title != null && l.Type is not ("title" or "secHead") && !NotForContent.IsMatch(l.Name)
                              && l.Content.W * l.Content.H >= 0.3 * SlideWidth * SlideHeight)
                  .OrderBy(l => l.Placeholders.Count).ThenByDescending(l => l.Content.W * l.Content.H).FirstOrDefault()
        ?? Layouts[0];

    static readonly System.Text.RegularExpressions.Regex NotForContent =
        new("section|cover|divider|closing|opening|title slide|agenda|contents", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// The layout for section header slides to go with <paramref name="content"/>: on the same master, the first
    /// Section Header layout, else the first with a title whose name says it's a section or divider; null if none.
    /// </summary>
    public LayoutInfo? SectionLayoutFor(LayoutInfo content) =>
        Layouts.FirstOrDefault(l => l.MasterIndex == content.MasterIndex && l.Type == "secHead")
        ?? Layouts.FirstOrDefault(l => l.MasterIndex == content.MasterIndex && l.Title != null && SectionName.IsMatch(l.Name));

    static readonly System.Text.RegularExpressions.Regex SectionName =
        new("section|divider", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public ThemeInfo ThemeOf(LayoutInfo layout) => Themes[layout.MasterIndex];

    public SlideFrame FrameOf(LayoutInfo layout) => new(SlideWidth, SlideHeight, layout.Content, layout.Title);
}

/// <summary>
/// One slide layout. <see cref="Content"/> is where diagrams go; <see cref="ContentSource"/> says how it was found.
/// <see cref="Placeholders"/> are all of the layout's placeholders (with inherited positions), for a wireframe preview.
/// </summary>
public sealed record LayoutInfo(int MasterIndex, int Index, string Name, string Type, Rect? Title, string TitleType, Rect Content, string ContentSource)
{
    public IReadOnlyList<(string Kind, Rect Bounds)> Placeholders { get; init; } = [];
    /// <summary>The layout's first text placeholder other than the title (a section header's description), if any.</summary>
    public (string Type, uint Idx)? Text { get; init; }
    public override string ToString() => Name;
}

public static class TemplateReader
{
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    const double Inset = 0.1 * Emu.PerInch, Margin = 0.5 * Emu.PerInch;

    public static TemplateInfo Read(string path) => Read(File.ReadAllBytes(path), Path.GetFileName(path));

    public static TemplateInfo Read(byte[] bytes, string name)
    {
        using var doc = Open(bytes, name);
        return Read(doc, name);
    }

    /// <summary>Opens a copy of a template (.potx or .pptx) in memory, editable, as a presentation.</summary>
    internal static PresentationDocument Open(byte[] bytes, string name)
    {
        var ms = new MemoryStream();
        ms.Write(bytes);
        ms.Position = 0;
        PresentationDocument doc;
        try { doc = PresentationDocument.Open(ms, true); }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FormatException)
        {
            throw new InvalidDataException($"{name} isn't a PowerPoint file ({ex.Message}).", ex);
        }
        if (doc.DocumentType is PresentationDocumentType.MacroEnabledPresentation or PresentationDocumentType.MacroEnabledTemplate or PresentationDocumentType.AddIn)
        {
            doc.Dispose();
            throw new InvalidDataException($"{name} is macro-enabled; save it as .potx or .pptx first.");
        }
        if (doc.DocumentType != PresentationDocumentType.Presentation) doc.ChangeDocumentType(PresentationDocumentType.Presentation);
        if (doc.PresentationPart?.Presentation?.SlideSize == null) { doc.Dispose(); throw new InvalidDataException($"{name} has no slide size."); }
        return doc;
    }

    internal static TemplateInfo Read(PresentationDocument doc, string name)
    {
        var pp = doc.PresentationPart!;
        var pres = pp.Presentation!;
        double sw = pres.SlideSize!.Cx!.Value, sh = pres.SlideSize.Cy!.Value;
        var themes = new List<ThemeInfo>();
        var layouts = new List<LayoutInfo>();
        var masters = MasterParts(pp);
        for (var mi = 0; mi < masters.Count; mi++)
        {
            var master = masters[mi];
            var mx = Load(master);
            themes.Add(Theme(mx, master.ThemePart));
            var masterPhs = Placeholders(mx);
            var li = 0;
            foreach (var lp in LayoutParts(master))
            {
                var lx = Load(lp);
                var type = (string?)lx.Root!.Attribute("type") ?? "cust";
                var lname = (string?)lx.Root.Element(P + "cSld")?.Attribute("name") ?? $"Layout {li + 1}";
                var phs = Placeholders(lx).Select(p => p with { Bounds = p.Bounds ?? Inherit(p, masterPhs) }).ToList();
                var title = phs.FirstOrDefault(p => p.Kind == "title");
                var (content, how) = ContentRect(phs, masterPhs, title?.Bounds, sw, sh);
                layouts.Add(new LayoutInfo(mi, li, lname, type, title?.Bounds, title?.Type ?? "title", content, how)
                {
                    Placeholders = phs.Where(p => p.Bounds != null).Select(p => (p.Kind, p.Bounds!.Value)).ToList(),
                    Text = phs.Where(p => p.Type is "body" or "subTitle" or "obj").Select(p => ((string Type, uint Idx)?)(p.Type, p.Idx)).FirstOrDefault(),
                });
                li++;
            }
        }
        if (layouts.Count == 0) throw new InvalidDataException($"{name} has no slide layouts.");
        return new TemplateInfo
        {
            Name = name, SlideWidth = sw, SlideHeight = sh, Themes = themes, Layouts = layouts,
            ExistingSlides = pres.SlideIdList?.Count() ?? 0,
        };
    }

    internal static List<SlideMasterPart> MasterParts(PresentationPart pp) =>
        pp.Presentation!.SlideMasterIdList!.Elements<SlideMasterId>().Select(id => (SlideMasterPart)pp.GetPartById(id.RelationshipId!)).ToList();

    internal static List<SlideLayoutPart> LayoutParts(SlideMasterPart m) =>
        m.SlideMaster!.SlideLayoutIdList?.Elements<SlideLayoutId>().Select(id => (SlideLayoutPart)m.GetPartById(id.RelationshipId!)).ToList() ?? [];

    /// <summary>
    /// The content rectangle: the union of the layout's body placeholders, ignoring any under 5% of the slide;
    /// else the master's body placeholder, which stays clear of the master's artwork; else the slide less margins.
    /// Then clipped against the title on whichever side it sits, and inset 0.1 in.
    /// </summary>
    static (Rect, string) ContentRect(List<Ph> phs, List<Ph> masterPhs, Rect? title, double sw, double sh)
    {
        var bodies = phs.Where(p => p.Kind == "body" && p.Bounds is Rect b && b.W * b.H >= 0.05 * sw * sh).Select(p => p.Bounds!.Value).ToList();
        Rect content;
        string how;
        if (bodies.Count > 0) { content = bodies.Aggregate(Rect.Union); how = bodies.Count == 1 ? "layout's content placeholder" : $"layout's {bodies.Count} content placeholders"; }
        else if (masterPhs.FirstOrDefault(p => p.Kind == "body" && p.Bounds != null)?.Bounds is Rect mb) { content = mb; how = "master's content placeholder"; }
        else { content = new Rect(Margin, Margin, sw - 2 * Margin, sh - 2 * Margin); how = "slide margins"; }

        if (title is Rect t && content.Intersects(t))
        {
            // Clip on the side the title is on: above/below by centre for wide titles, left/right for tall ones.
            if (t.H > t.W)
                content = t.CenterX > content.CenterX ? content with { W = t.X - Inset - content.X } : new Rect(t.Right + Inset, content.Y, content.Right - t.Right - Inset, content.H);
            else
                content = t.CenterY < content.CenterY ? new Rect(content.X, t.Bottom + Inset, content.W, content.Bottom - t.Bottom - Inset) : content with { H = t.Y - Inset - content.Y };
            how += ", kept clear of the title";
        }
        return (content.Inset(Inset), how);
    }

    static Rect? Inherit(Ph ph, List<Ph> master) =>
        master.FirstOrDefault(m => m.Kind == ph.Kind && m.Bounds != null)?.Bounds
        ?? master.FirstOrDefault(m => m.Idx == ph.Idx && m.Bounds != null)?.Bounds;

    sealed record Ph(string Type, string Kind, uint Idx, Rect? Bounds);

    static List<Ph> Placeholders(XDocument d) => d.Descendants(P + "sp")
        .Select(sp => (sp, ph: sp.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph")))
        .Where(t => t.ph != null)
        .Select(t =>
        {
            var type = (string?)t.ph!.Attribute("type") ?? "obj";
            var kind = type switch { "title" or "ctrTitle" => "title", "body" or "obj" => "body", _ => type };
            var x = t.sp.Element(P + "spPr")?.Element(A + "xfrm");
            Rect? r = x?.Element(A + "off") is { } off && x.Element(A + "ext") is { } ext
                ? new Rect((long)off.Attribute("x")!, (long)off.Attribute("y")!, (long)ext.Attribute("cx")!, (long)ext.Attribute("cy")!)
                : null;
            return new Ph(type, kind, (uint?)t.ph.Attribute("idx") ?? 0, r);
        }).ToList();

    static ThemeInfo Theme(XDocument master, ThemePart? themePart)
    {
        if (themePart == null) return ThemeInfo.OfficeDefault;
        var theme = Load(themePart);
        var scheme = theme.Descendants(A + "clrScheme").FirstOrDefault()?.Elements()
            .ToDictionary(e => e.Name.LocalName, e => (string?)e.Element(A + "srgbClr")?.Attribute("val") ?? (string?)e.Element(A + "sysClr")?.Attribute("lastClr") ?? "808080")
            ?? [];
        var map = master.Root!.Element(P + "clrMap")?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value) ?? [];
        string Slot(string name) => scheme.GetValueOrDefault(map.GetValueOrDefault(name, name), scheme.GetValueOrDefault(name, "808080"));
        var colors = new Dictionary<SchemeSlot, string>
        {
            [SchemeSlot.Tx1] = Slot("tx1"), [SchemeSlot.Bg1] = Slot("bg1"), [SchemeSlot.Tx2] = Slot("tx2"), [SchemeSlot.Bg2] = Slot("bg2"),
            [SchemeSlot.Hlink] = Slot("hlink"),
        };
        for (var i = 1; i <= 6; i++) colors[SchemeSlot.Accent1 + i - 1] = Slot($"accent{i}");
        string Font(string which) => (string?)theme.Descendants(A + which).FirstOrDefault()?.Element(A + "latin")?.Attribute("typeface") ?? "Calibri";
        return new ThemeInfo((string?)theme.Root!.Attribute("name") ?? "", colors, Font("majorFont"), Font("minorFont"));
    }

    internal static XDocument Load(OpenXmlPart part) { using var s = part.GetStream(); return XDocument.Load(s); }
}
