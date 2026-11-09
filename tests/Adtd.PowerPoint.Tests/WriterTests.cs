using System.Xml.Linq;
using Adtd.Imaging;
using Adtd.Layout;
using DocumentFormat.OpenXml.Packaging;

namespace Adtd.PowerPoint.Tests;

public class TemplateReaderTests
{
    const double In = Emu.PerInch, Inset = 0.1 * Emu.PerInch;

    [Fact]
    public void Reads_size_layouts_theme_and_fonts_from_a_potx()
    {
        var t = TemplateReader.Read(TestTemplates.Basic(), "basic.potx");
        Assert.Equal(TestTemplates.W, t.SlideWidth);
        Assert.Equal(TestTemplates.H, t.SlideHeight);
        Assert.Equal(["Title Slide", "Title and Content", "Title Only", "Diagram", "Blank", "Section Header"], t.Layouts.Select(l => l.Name));
        var theme = t.Themes.Single();
        Assert.Equal("Georgia", theme.MajorFont);
        Assert.Equal("Verdana", theme.MinorFont);
        Assert.Equal("4472C4", theme.Colors[SchemeSlot.Accent1]);
        Assert.Equal("FAFAFA", theme.Colors[SchemeSlot.Bg1]);
        Assert.Equal("111111", theme.Colors[SchemeSlot.Tx1]);
    }

    [Fact]
    public void Resolves_dark_colour_maps()
    {
        var theme = TemplateReader.Read(TestTemplates.Basic(dark: true), "dark.potx").Themes.Single();
        Assert.Equal("111111", theme.Colors[SchemeSlot.Bg1]);
        Assert.Equal("FAFAFA", theme.Colors[SchemeSlot.Tx1]);
        Assert.Equal("EEEEDD", theme.Colors[SchemeSlot.Tx2]);
    }

    [Fact]
    public void Finds_the_content_area_by_the_M0_rules()
    {
        var t = TemplateReader.Read(TestTemplates.Basic(), "basic.potx");
        var mb = TestTemplates.MasterBody;
        var masterBody = new Rect(mb.X + Inset, mb.Y + Inset, mb.Cx - 2 * Inset, mb.Cy - 2 * Inset);

        // Inherited from the master's body placeholder.
        var tc = t.Find("Title and Content")!;
        Assert.Equal(masterBody, tc.Content);
        Assert.Equal(new Rect(TestTemplates.MasterTitle.X, TestTemplates.MasterTitle.Y, TestTemplates.MasterTitle.Cx, TestTemplates.MasterTitle.Cy), tc.Title);
        Assert.Equal("layout's content placeholder", tc.ContentSource);

        // No body placeholder: the master's body, not the slide margins.
        Assert.Equal(masterBody, t.Find("Title Only")!.Content);
        Assert.Equal("master's content placeholder", t.Find("Title Only")!.ContentSource);

        // A hairline body placeholder is ignored, and the title (own position) clips the master body from above.
        var d = t.Find("Diagram")!;
        Assert.Equal("master's content placeholder", d.ContentSource);
        Assert.Equal(masterBody, d.Content);   // the title ends above the master body, so no clipping

        Assert.Null(t.Find("Blank")!.Title);
        Assert.Equal("ctrTitle", t.Find("Title Slide")!.TitleType);
    }

    [Fact]
    public void Keeps_clear_of_a_title_down_the_side()
    {
        var l = TemplateReader.Read(TestTemplates.VerticalTitle(), "vertical.potx").Layouts.Single();
        Assert.True(l.Title!.Value.H > l.Title.Value.W);
        Assert.True(l.Content.Right <= l.Title.Value.X - Inset + 1);
        Assert.Contains("clear of the title", l.ContentSource);
    }

    [Fact]
    public void Defaults_to_title_only()
    {
        Assert.Equal("Title Only", TemplateReader.Read(TestTemplates.Basic(), "basic.potx").DefaultLayout.Name);
        Assert.Equal("Heading Only", TemplateReader.Read(TestTemplates.VerticalTitle(), "v.potx").DefaultLayout.Name);
    }

    [Fact]
    public void Refuses_files_that_are_not_presentations()
    {
        var ex = Assert.Throws<InvalidDataException>(() => TemplateReader.Read("not a zip"u8.ToArray(), "notes.txt"));
        Assert.Contains("notes.txt isn't a PowerPoint file", ex.Message);
    }
}

public class ComposerTests
{
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    static readonly IconAsset Icon = IconAsset.Default();

    static (PresentationDocument Doc, ComposeReport Report) Compose(byte[] template, string layout, DeckPlan plan, ComposeOptions? options = null)
    {
        var t = TemplateReader.Read(template, "t.potx");
        var ms = new MemoryStream();
        var report = PptxComposer.Write(ms, template, "t.potx", t.Find(layout)!, plan, new IconImage(Icon.Svg, Icon.Png), options);
        ms.Position = 0;
        return (PresentationDocument.Open(ms, false), report);
    }

    static XDocument Xml(OpenXmlPart part) { using var s = part.GetStream(); return XDocument.Load(s); }

    static List<SlidePart> Slides(PresentationDocument doc) =>
        doc.PresentationPart!.Presentation!.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            .Select(s => (SlidePart)doc.PresentationPart.GetPartById(s.RelationshipId!)).ToList();

    static DeckPlan ShowcasePlan(byte[] template, string layout)
    {
        var t = TemplateReader.Read(template, "t.potx");
        var l = t.Find(layout)!;
        return Showcase.Build(t.FrameOf(l), t.ThemeOf(l), Icon.Aspect, l.Name);
    }

    [Fact]
    public void Writes_a_valid_deck_on_the_chosen_layout_and_removes_the_templates_slides()
    {
        var template = TestTemplates.Basic(withSlides: true);
        var plan = ShowcasePlan(template, "Title Only");
        var twice = plan with { Slides = [plan.Slides[0], plan.Slides[0] with { Title = "Second" }] };
        var (doc, report) = Compose(template, "Title Only", twice);
        using var _ = doc;

        Assert.Empty(report.ValidationErrors);
        Assert.Equal(2, report.Slides);
        var slides = Slides(doc);
        Assert.Equal(2, slides.Count);
        Assert.All(slides, s => Assert.Equal("Title Only", Xml(s.SlideLayoutPart!).Root!.Element(P + "cSld")!.Attribute("name")!.Value));
        var pres = Xml(doc.PresentationPart!);
        Assert.Empty(pres.Descendants(P + "custShowLst"));
        Assert.Empty(pres.Descendants().Where(e => e.Name.LocalName == "sectionLst"));
        Assert.Equal(DocumentFormat.OpenXml.PresentationDocumentType.Presentation, doc.DocumentType);
    }

    [Fact]
    public void Puts_the_title_in_the_layouts_title_placeholder()
    {
        var (doc, _) = Compose(TestTemplates.Basic(), "Title Only", new DeckPlan([new SlidePlan("Sites overview", [])]));
        using var _d = doc;
        var title = Xml(Slides(doc)[0]).Descendants(P + "sp").First();
        Assert.Equal("title", title.Descendants(P + "ph").Single().Attribute("type")!.Value);
        Assert.Empty(title.Element(P + "spPr")!.Elements());            // position and style come from the layout
        Assert.Equal("Sites overview", string.Concat(title.Descendants(A + "t").Select(t => t.Value)));
    }

    [Fact]
    public void Writes_a_title_box_when_the_layout_has_no_title()
    {
        var (doc, report) = Compose(TestTemplates.Basic(), "Blank", new DeckPlan([new SlidePlan("Domains", [])]));
        using var _ = doc;
        Assert.Empty(report.ValidationErrors);
        var title = Xml(Slides(doc)[0]).Descendants(P + "sp").First();
        Assert.Empty(title.Descendants(P + "ph"));
        Assert.Equal("1", title.Descendants(P + "cNvSpPr").Single().Attribute("txBox")!.Value);
        Assert.Equal("+mj-lt", title.Descendants(A + "latin").First().Attribute("typeface")!.Value);
    }

    [Fact]
    public void Colours_come_from_the_theme_and_fonts_from_the_theme_fonts()
    {
        var template = TestTemplates.Basic();
        var (doc, _) = Compose(template, "Title Only", ShowcasePlan(template, "Title Only"));
        using var _d = doc;
        var x = Xml(Slides(doc)[0]);
        Assert.Empty(x.Descendants(A + "srgbClr"));
        Assert.NotEmpty(x.Descendants(A + "schemeClr").Where(e => e.Attribute("val")!.Value == "accent6"));
        Assert.Contains(x.Descendants(A + "lumMod"), e => e.Attribute("val")!.Value == "75000");
        Assert.All(x.Descendants(A + "latin"), e => Assert.StartsWith("+m", e.Attribute("typeface")!.Value));
    }

    [Fact]
    public void Connectors_are_glued_to_shapes_on_the_slide()
    {
        var template = TestTemplates.Basic();
        var (doc, report) = Compose(template, "Title Only", ShowcasePlan(template, "Title Only"));
        using var _ = doc;
        Assert.DoesNotContain(report.Warnings, w => w.Contains("glued") || w.Contains("route"));
        var x = Xml(Slides(doc)[0]);
        var ids = x.Descendants(P + "cNvPr").Select(e => e.Attribute("id")!.Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var cxns = x.Descendants(P + "cxnSp").ToList();
        Assert.Equal(4, cxns.Count);
        foreach (var c in cxns)
            foreach (var end in new[] { "stCxn", "endCxn" })
                Assert.Contains(c.Descendants(A + end).Single().Attribute("id")!.Value, ids);
        Assert.Contains(cxns, c => c.Descendants(A + "prstGeom").Single().Attribute("prst")!.Value == "bentConnector3");
        Assert.Contains(cxns, c => c.Descendants(A + "prstGeom").Single().Attribute("prst")!.Value == "straightConnector1");
    }

    [Fact]
    public void Embeds_the_icon_once_as_SVG_with_a_PNG_fallback()
    {
        var template = TestTemplates.Basic();
        var plan = ShowcasePlan(template, "Title Only");
        var (doc, _) = Compose(template, "Title Only", plan with { Slides = [plan.Slides[0], plan.Slides[0], plan.Slides[0]] });
        using var _d = doc;
        var images = Slides(doc).SelectMany(s => s.ImageParts).Distinct().ToList();
        Assert.Equal(2, images.Count);
        Assert.Contains(images, i => i.ContentType == "image/svg+xml");
        Assert.Contains(images, i => i.ContentType == "image/png");
        var blip = Xml(Slides(doc)[1]).Descendants(A + "blip").First();
        Assert.NotNull(blip.Descendants().SingleOrDefault(e => e.Name.LocalName == "svgBlip"));
    }

    [Fact]
    public void Keeps_existing_slides_in_their_sections_and_adds_a_section_for_the_new_ones()
    {
        var template = TestTemplates.Basic(withSlides: true);
        var (doc, report) = Compose(template, "Title Only", new DeckPlan([new SlidePlan("A", []), new SlidePlan("B", [])]), new ComposeOptions { KeepExistingSlides = true });
        using var _ = doc;
        Assert.Empty(report.ValidationErrors);
        Assert.Equal(4, Slides(doc).Count);
        var sections = Xml(doc.PresentationPart!).Descendants().Where(e => e.Name.LocalName == "section").ToList();
        Assert.Equal(["Intro", "Body", "AD topology"], sections.Select(s => s.Attribute("name")!.Value));
        Assert.Equal(["258", "259"], sections[2].Descendants().Where(e => e.Name.LocalName == "sldId").Select(e => e.Attribute("id")!.Value));
    }

    [Fact]
    public void Table_columns_add_up_to_the_table_width()
    {
        var table = new TableItem("Subnets", new Rect(1000000, 2000000, 4000001, 0), ["Prefix", "Site", "Notes"], [["10.0.0.0/8", "Hub", ""]], 10)
        { ColumnWeights = [1, 1, 3] };
        var (doc, report) = Compose(TestTemplates.Basic(), "Title Only", new DeckPlan([new SlidePlan("T", [table])]));
        using var _ = doc;
        Assert.Empty(report.ValidationErrors);
        var cols = Xml(Slides(doc)[0]).Descendants(A + "gridCol").Select(c => long.Parse(c.Attribute("w")!.Value)).ToList();
        Assert.Equal(4000001, cols.Sum());
        Assert.Equal(3, cols.Count);
    }

    [Fact]
    public void Warns_about_links_to_shapes_that_are_not_there()
    {
        var box = new BoxItem("A", new Rect(0, 0, 100000, 100000), Geometry.Rect) { Key = "a" };
        var link = new LinkItem("A-B", "a", "b", [new Point(100000, 50000), new Point(300000, 50000)], new Stroke(new ThemeColor(SchemeSlot.Tx1), 1));
        var (doc, report) = Compose(TestTemplates.Basic(), "Title Only", new DeckPlan([new SlidePlan("T", [box, link])]));
        using var _ = doc;
        Assert.Contains(report.Warnings, w => w.Contains("nothing on the slide has key b"));
        Assert.Empty(report.ValidationErrors);
    }

    /// <summary>The Office templates (tools/New-TestTemplates.ps1) and your own (samples/local), when present on this machine.</summary>
    [Fact]
    public void Showcase_is_valid_on_every_local_template()
    {
        var root = FindRoot();
        var files = new[] { Path.Combine(root, "out", "test-templates"), Path.Combine(root, "samples", "local") }
            .Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d)).Where(f => f.EndsWith(".potx") || f.EndsWith(".pptx")).ToList();
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(f);
            var t = TemplateReader.Read(bytes, Path.GetFileName(f));
            var l = t.DefaultLayout;
            var ms = new MemoryStream();
            var report = PptxComposer.Write(ms, bytes, Path.GetFileName(f), l, Showcase.Build(t.FrameOf(l), t.ThemeOf(l), Icon.Aspect, l.Name), new IconImage(Icon.Svg, Icon.Png));
            Assert.True(report.ValidationErrors.Count == 0, $"{Path.GetFileName(f)}: {string.Join("; ", report.ValidationErrors)}");
        }
    }

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Adtd.sln"))) d = d.Parent;
        return d?.FullName ?? AppContext.BaseDirectory;
    }
}

public class RouteTests
{
    static string Prst(params (double X, double Y)[] pts) =>
        XElement.Parse($"<r xmlns:a=\"a\">{Routes.Match(pts.Select(p => new Point(p.X, p.Y)).ToList())!.Value.Geometry}</r>").Elements().Single().Attribute("prst")!.Value;

    [Fact]
    public void Matches_straight_lines_in_any_direction()
    {
        Assert.Equal("straightConnector1", Prst((0, 0), (1000, 500)));
        Assert.Contains("flipH=\"1\"", Routes.Match([new Point(1000, 0), new Point(0, 500)])!.Value.Xfrm);
    }

    [Fact]
    public void Matches_elbows()
    {
        Assert.Equal("bentConnector3", Prst((0, 0), (500, 0), (500, 800), (1000, 800)));      // across, down, across
        Assert.Equal("bentConnector3", Prst((0, 0), (0, 400), (900, 400), (900, 800)));       // down, across, down (rotated)
        Assert.Equal("bentConnector2", Prst((0, 0), (600, 0), (600, 600)));
        Assert.Equal("bentConnector4", Prst((0, 0), (300, 0), (300, 500), (900, 500), (900, 200)));
    }

    [Fact]
    public void Refuses_routes_with_diagonal_segments() =>
        Assert.Null(Routes.Match([new Point(0, 0), new Point(500, 300), new Point(900, 300)]));
}

public class ColorTests
{
    [Fact]
    public void Applies_luminance_like_PowerPoint()
    {
        Assert.Equal("2F5597", ColorMath.Apply("4472C4", 75000, 0));        // "Accent 1, Darker 25%"
        Assert.Equal("B4C7E7", ColorMath.Apply("4472C4", 40000, 60000));    // "Accent 1, Lighter 60%"
    }

    [Fact]
    public void Picks_readable_text()
    {
        Assert.Equal(21, ColorMath.Contrast("000000", "FFFFFF"), 3);
        var theme = ThemeInfo.OfficeDefault;
        Assert.Equal(SchemeSlot.Bg1, theme.TextOn(ThemeColor.Accent(1)).Slot);                    // dark blue: white text
        Assert.Equal(SchemeSlot.Tx1, theme.TextOn(ThemeColor.Accent(4).Lighter(0.6)).Slot);       // pale: black text
    }
}

public class IconTests
{
    [Fact]
    public void Loads_the_default_icon_with_a_512px_fallback()
    {
        var icon = IconAsset.Default();
        Assert.Equal(112.0 / 139, icon.Aspect, 3);
        using var bmp = SkiaSharp.SKBitmap.Decode(icon.Png);
        Assert.Equal(512, bmp.Height);
        Assert.Empty(icon.Warnings);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><script>alert(1)</script></svg>", "script")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 10 10\"><image xlink:href=\"http://example.com/x.png\"/></svg>", "outside file")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><foreignObject/></svg>", "foreignObject")]
    [InlineData("<html/>", "isn't an SVG")]
    [InlineData("<svg", "isn't valid SVG")]
    public void Refuses_unsafe_or_broken_SVGs(string svg, string reason)
    {
        var ex = Assert.Throws<InvalidDataException>(() => IconAsset.FromSvg(System.Text.Encoding.UTF8.GetBytes(svg), "x.svg"));
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Warns_about_features_PowerPoint_may_not_draw()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 10\"><defs><filter id=\"f\"><feGaussianBlur stdDeviation=\"1\"/></filter></defs><rect width=\"20\" height=\"10\" filter=\"url(#f)\"/></svg>";
        var icon = IconAsset.FromSvg(System.Text.Encoding.UTF8.GetBytes(svg), "blur.svg");
        Assert.Contains(icon.Warnings, w => w.Contains("filters"));
        Assert.Equal(2, icon.Aspect, 3);
    }

    [Fact]
    public void Accepts_PNG_icons_and_flags_small_ones()
    {
        var png = IconAsset.Default().Png;
        var big = IconAsset.FromPng(png, "dc.png");
        Assert.Null(big.Svg);
        Assert.Empty(big.Warnings);
        using var small = new SkiaSharp.SKBitmap(64, 64);
        using var data = SkiaSharp.SKImage.FromBitmap(small).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        Assert.Contains("soft", IconAsset.FromPng(data.ToArray(), "tiny.png").Warnings.Single());
    }
}
