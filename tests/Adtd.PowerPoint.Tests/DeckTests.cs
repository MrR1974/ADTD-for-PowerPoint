using System.Xml.Linq;
using Adtd.Imaging;
using Adtd.Layout;
using Adtd.Model.Samples;
using DocumentFormat.OpenXml.Packaging;

namespace Adtd.PowerPoint.Tests;

/// <summary>Whole decks: a forest laid out by <see cref="DeckPlanner"/> and written into a template.</summary>
public class DeckTests
{
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    static readonly IconAsset Icon = IconAsset.Default();

    static (PresentationDocument Doc, ComposeReport Report, DeckPlan Plan) Render(byte[] template, string forest, bool sectionLayout = true)
    {
        var t = TemplateReader.Read(template, "t.potx");
        var layout = t.DefaultLayout;
        var plan = DeckPlanner.Plan(SampleForests.ByName(forest), t.FrameOf(layout), t.ThemeOf(layout), ApproxTextMeasurer.Instance, Icon.Aspect);
        var ms = new MemoryStream();
        var report = PptxComposer.Write(ms, template, "t.potx", layout, plan, new IconImage(Icon.Svg, Icon.Png),
                                        new ComposeOptions { SectionLayout = sectionLayout ? t.SectionLayoutFor(layout) : null });
        ms.Position = 0;
        return (PresentationDocument.Open(ms, false), report, plan);
    }

    static IEnumerable<XDocument> Slides(PresentationDocument doc) =>
        doc.PresentationPart!.Presentation!.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            .Select(s => doc.PresentationPart.GetPartById(s.RelationshipId!))
            .Select(p => { using var s = p.GetStream(); return XDocument.Load(s); });

    [Theory]
    [InlineData("single", false)]
    [InlineData("contoso", false)]
    [InlineData("contoso", true)]
    public void A_forest_becomes_a_valid_deck_with_every_connector_glued(string forest, bool dark)
    {
        var (doc, report, plan) = Render(TestTemplates.Basic(dark: dark), forest);
        using var _ = doc;
        Assert.Empty(report.ValidationErrors);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("glued") || w.Contains("route") || w.Contains("key"));
        Assert.Equal(plan.Slides.Count, report.Slides);
        foreach (var x in Slides(doc))
        {
            var ids = x.Descendants(P + "cNvPr").Select(e => e.Attribute("id")!.Value).ToHashSet();
            foreach (var c in x.Descendants(P + "cxnSp"))
                foreach (var end in new[] { "stCxn", "endCxn" })
                    Assert.Contains(c.Descendants(A + end).Single().Attribute("id")!.Value, ids);
            Assert.Empty(x.Descendants(A + "srgbClr"));
            Assert.All(x.Descendants(A + "latin"), e => Assert.StartsWith("+m", e.Attribute("typeface")!.Value));
        }
        // One SVG and one PNG, shared by every slide with a DC card.
        var images = doc.PresentationPart!.SlideParts.SelectMany(s => s.ImageParts).Distinct().ToList();
        Assert.Equal(["image/png", "image/svg+xml"], images.Select(i => i.ContentType).Order());
    }

    static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";

    static XDocument Presentation(PresentationDocument doc) { using var s = doc.PresentationPart!.GetStream(); return XDocument.Load(s); }

    [Fact]
    public void Writes_the_forest_and_the_sites_as_sections()
    {
        var (doc, report, plan) = Render(TestTemplates.Basic(), "single");
        using var _ = doc;
        Assert.Empty(report.ValidationErrors);
        var pres = Presentation(doc);
        var sections = pres.Descendants(P14 + "section").ToList();
        Assert.Equal([DeckPlanner.ForestSection, DeckPlanner.SitesSection], sections.Select(s => s.Attribute("name")!.Value));
        var ids = pres.Descendants(P + "sldId").Select(s => s.Attribute("id")!.Value).ToList();
        Assert.Equal(ids, sections.SelectMany(s => s.Descendants(P14 + "sldId")).Select(s => s.Attribute("id")!.Value));
        Assert.Equal(plan.Slides.Count(s => s.Section == DeckPlanner.ForestSection), sections[0].Descendants(P14 + "sldId").Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_sites_open_with_a_section_header_on_the_section_layout(bool sectionLayout)
    {
        var (doc, report, plan) = Render(TestTemplates.Basic(), "contoso", sectionLayout);
        using var _ = doc;
        Assert.Empty(report.ValidationErrors);
        var i = plan.Slides.ToList().FindIndex(s => s.Kind == SlideKind.SectionHeader && s.Title == "Sites");
        Assert.Equal(DeckPlanner.SitesSection, plan.Slides[i].Section);
        Assert.Equal(SlideKind.SectionHeader, plan.Slides[0].Kind);
        Assert.Equal(DeckPlanner.ForestSection, plan.Slides[0].Section);
        var part = doc.PresentationPart!.GetPartById(doc.PresentationPart.Presentation!.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ElementAt(i).RelationshipId!);
        var layoutName = ((SlidePart)part).SlideLayoutPart!.SlideLayout!.CommonSlideData!.Name!.Value;
        var x = Slides(doc).ElementAt(i);
        var texts = x.Descendants(A + "t").Select(t => t.Value).ToList();
        Assert.Equal(["Sites", plan.Slides[i].Subtitle!], texts);
        if (sectionLayout)
        {
            Assert.Equal("Section Header", layoutName);
            Assert.Contains(x.Descendants(P + "ph"), p => (string?)p.Attribute("type") == "body" && (string?)p.Attribute("idx") == "1");
        }
        else Assert.Equal("Title Only", layoutName);
    }

    [Fact]
    public void A_section_layout_with_only_a_title_gets_only_the_title()
    {
        var template = TestTemplates.Basic();
        var t = TemplateReader.Read(template, "t.potx");
        var plan = new DeckPlan([new SlidePlan("Sites", []) { Kind = SlideKind.SectionHeader, Subtitle = "3 sites" }]);
        var ms = new MemoryStream();
        var report = PptxComposer.Write(ms, template, "t.potx", t.DefaultLayout, plan, null, new ComposeOptions { SectionLayout = t.Find("Title Only") });
        Assert.Empty(report.ValidationErrors);
        ms.Position = 0;
        using var doc = PresentationDocument.Open(ms, false);
        Assert.Equal(["Sites"], Slides(doc).Single().Descendants(A + "t").Select(e => e.Value));
    }

    [Fact]
    public void Kept_template_slides_keep_their_sections_and_the_new_ones_follow()
    {
        var template = TestTemplates.Basic(withSlides: true);
        var t = TemplateReader.Read(template, "t.potx");
        var plan = new DeckPlan([new SlidePlan("A", []) { Section = "One" }, new SlidePlan("B", []) { Section = "Two" }]);
        var ms = new MemoryStream();
        var report = PptxComposer.Write(ms, template, "t.potx", t.Find("Title Only")!, plan, null, new ComposeOptions { KeepExistingSlides = true });
        Assert.Empty(report.ValidationErrors);
        ms.Position = 0;
        using var doc = PresentationDocument.Open(ms, false);
        var pres = Presentation(doc);
        var sections = pres.Descendants(P14 + "section").ToList();
        Assert.Equal(["One", "Two"], sections.TakeLast(2).Select(s => s.Attribute("name")!.Value));
        // Every slide is in exactly one section.
        Assert.Equal(pres.Descendants(P + "sldId").Select(s => s.Attribute("id")!.Value).Order(),
                     sections.SelectMany(s => s.Descendants(P14 + "sldId")).Select(s => s.Attribute("id")!.Value).Order());
    }

    [Fact]
    public void Measures_the_theme_fonts_or_warns_and_allows_for_a_stand_in()
    {
        using var real = new SkiaTextMeasurer(ThemeInfo.OfficeDefault with { MajorFont = "Arial", MinorFont = "Arial" });
        Assert.Empty(real.Warnings);
        var w10 = real.Width("Domain controllers", 10);
        Assert.Equal(2 * w10, real.Width("Domain controllers", 20), 2 * w10 * 0.01);
        Assert.True(real.Width("Bold", 10, bold: true) > real.Width("Bold", 10));
        Assert.InRange(real.LineHeight(10), 10 * 1.15 * Emu.PerPoint, 10 * 1.3 * Emu.PerPoint);

        using var missing = new SkiaTextMeasurer(ThemeInfo.OfficeDefault with { MinorFont = "No Such Font 123" });
        Assert.Contains(missing.Warnings, w => w.Contains("No Such Font 123"));
    }
}
