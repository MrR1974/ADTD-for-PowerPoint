using System.Text;
using Adtd.Layout;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Validation;
using P14 = DocumentFormat.OpenXml.Office2010.PowerPoint;

namespace Adtd.PowerPoint;

/// <summary>The DC icon as it's embedded: the SVG (drawn by PowerPoint 2016 and later) and a PNG for older readers.</summary>
public sealed record IconImage(byte[]? Svg, byte[] Png);

public sealed record ComposeOptions
{
    /// <summary>Keep the template's own slides and add the diagrams after them (in a section of their own, if it has sections).</summary>
    public bool KeepExistingSlides { get; init; }
    /// <summary>The layout for section header slides (see <see cref="TemplateInfo.SectionLayoutFor"/>); without one they go on the content layout.</summary>
    public LayoutInfo? SectionLayout { get; init; }
}

public sealed record ComposeReport(int Slides, IReadOnlyList<string> Warnings, IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Writes a <see cref="DeckPlan"/> into a copy of a template: every slide on the chosen layout, its title in the
/// layout's title placeholder, its diagram as native shapes coloured from the theme. The template's masters, layouts
/// and theme are kept as they are.
/// </summary>
public static class PptxComposer
{
    const string SectionsExt = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";

    public static ComposeReport Write(Stream output, byte[] template, string templateName, LayoutInfo layout, DeckPlan plan,
                                      IconImage? icon, ComposeOptions? options = null)
    {
        options ??= new ComposeOptions();
        var warnings = new List<string>(plan.Warnings);
        var baseline = new HashSet<string>();
        byte[] result;
        using (var doc = TemplateReader.Open(template, templateName))
        {
            baseline.UnionWith(Validate(doc));
            var pp = doc.PresentationPart!;
            var pres = pp.Presentation!;
            SlideLayoutPart PartOf(LayoutInfo l) => TemplateReader.LayoutParts(TemplateReader.MasterParts(pp)[l.MasterIndex])[l.Index];
            var layoutPart = PartOf(layout);
            var sectionLayout = options.SectionLayout;
            var sectionPart = sectionLayout != null ? PartOf(sectionLayout) : null;

            if (!options.KeepExistingSlides) RemoveSlides(pp);
            pres.SlideIdList ??= pres.InsertAfter(new SlideIdList(),
                (OpenXmlElement?)pres.HandoutMasterIdList ?? (OpenXmlElement?)pres.NotesMasterIdList ?? pres.SlideMasterIdList!)!;

            uint nextId = Math.Max(256, pres.SlideIdList.Elements<SlideId>().Select(s => s.Id?.Value ?? 0).DefaultIfEmpty(0u).Max() + 1);
            ImagePart? svgPart = null, pngPart = null;
            var existing = pres.SlideIdList.Elements<SlideId>().Select(x => x.Id!.Value).ToList();
            var added = new List<(uint Id, string? Section)>();
            foreach (var slide in plan.Slides)
            {
                var sp = pp.AddNewPart<SlidePart>();
                var header = slide.Kind == SlideKind.SectionHeader;
                var on = header && sectionLayout != null ? sectionLayout : layout;
                sp.AddPart(header && sectionPart != null ? sectionPart : layoutPart);
                var writer = new SlideWriter("rIdSvg", "rIdPng", icon?.Svg != null);
                var body = writer.Write(slide);
                warnings.AddRange(writer.Warnings);
                if (writer.UsesIcon)
                {
                    if (icon == null) throw new InvalidOperationException("The plan has icons but no icon image was given.");
                    if (pngPart == null)
                    {
                        pngPart = sp.AddNewPart<ImagePart>("image/png", "rIdPng");
                        using (var s = new MemoryStream(icon.Png)) pngPart.FeedData(s);
                        if (icon.Svg != null)
                        {
                            svgPart = sp.AddNewPart<ImagePart>("image/svg+xml", "rIdSvg");
                            using var s2 = new MemoryStream(icon.Svg);
                            svgPart.FeedData(s2);
                        }
                    }
                    else
                    {
                        sp.AddPart(pngPart, "rIdPng");
                        if (svgPart != null) sp.AddPart(svgPart, "rIdSvg");
                    }
                }
                var title = on.Title != null ? TitlePlaceholder(slide.Title, on.TitleType) : TitleBox(slide.Title, on);
                // The description goes in the section layout's text placeholder; a section layout without one gets the
                // title only, as its designer intended. With no section layout, it's a text box on the content layout.
                if (header && slide.Subtitle != null)
                    title += on != sectionLayout ? SubtitleBox(slide.Subtitle, on)
                           : on.Text is { } ph ? TextPlaceholder(slide.Subtitle, ph.Type, ph.Idx) : "";
                Feed(sp, $"<p:sld {Xml.Ns}><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" +
                         $"{title}{body}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
                pres.SlideIdList.Append(new SlideId { Id = nextId, RelationshipId = pp.GetIdOfPart(sp) });
                added.Add((nextId++, slide.Section));
            }
            WriteSections(pres, existing, added);
            pres.Save();
            doc.Save();
            result = Bytes(doc);
        }

        // Report only errors this write introduced: some templates aren't schema-valid to begin with.
        List<string> errors;
        using (var check = PresentationDocument.Open(new MemoryStream(result), false))
            errors = Validate(check).Where(e => !baseline.Contains(e)).Distinct().ToList();
        output.Write(result);
        return new ComposeReport(plan.Slides.Count, warnings, errors);
    }

    public static ComposeReport Write(string path, string templatePath, LayoutInfo layout, DeckPlan plan, IconImage? icon, ComposeOptions? options = null)
    {
        using var ms = new MemoryStream();
        var report = Write(ms, File.ReadAllBytes(templatePath), Path.GetFileName(templatePath), layout, plan, icon, options);
        File.WriteAllBytes(path, ms.ToArray());
        return report;
    }

    static byte[] Bytes(PresentationDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Clone(ms).Dispose();
        return ms.ToArray();
    }

    /// <summary>Removes every slide, and the sections and custom shows that list them (left behind, they make PowerPoint repair the file).</summary>
    static void RemoveSlides(PresentationPart pp)
    {
        var pres = pp.Presentation!;
        foreach (var sid in pres.SlideIdList?.Elements<SlideId>().ToList() ?? [])
        {
            var part = pp.GetPartById(sid.RelationshipId!);
            sid.Remove();
            pp.DeletePart(part);
        }
        pres.CustomShowList?.Remove();
        foreach (var ext in pres.PresentationExtensionList?.Elements<PresentationExtension>().Where(e => e.Uri == SectionsExt).ToList() ?? [])
            ext.Remove();
        if (pres.PresentationExtensionList is { } list && !list.HasChildren) list.Remove();
    }

    /// <summary>
    /// Puts the new slides in sections: one per run of slides with the same <see cref="SlidePlan.Section"/> ("AD topology"
    /// for slides without one). Sections are written when the plan names any, or when kept template slides already
    /// have sections (every slide must then be in one). Kept slides that weren't in a section get "Template slides".
    /// </summary>
    static void WriteSections(Presentation pres, List<uint> existing, List<(uint Id, string? Section)> added)
    {
        var sections = pres.PresentationExtensionList?.Elements<PresentationExtension>()
            .FirstOrDefault(e => e.Uri == SectionsExt)?.GetFirstChild<P14.SectionList>();
        if (added.Count == 0 || (sections == null && added.All(a => a.Section == null))) return;
        P14.Section Section(string name, IEnumerable<uint> ids) =>
            new(new P14.SectionSlideIdList(ids.Select(id => new P14.SectionSlideIdListEntry { Id = id })))
            { Name = name, Id = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}" };
        if (sections == null)
        {
            sections = new P14.SectionList();
            if (existing.Count > 0) sections.Append(Section("Template slides", existing));
            pres.PresentationExtensionList ??= new PresentationExtensionList();
            pres.PresentationExtensionList.Append(new PresentationExtension(sections) { Uri = SectionsExt });
        }
        var start = 0;
        for (var i = 1; i <= added.Count; i++)
        {
            if (i < added.Count && added[i].Section == added[start].Section) continue;
            sections.Append(Section(added[start].Section ?? "AD topology", added.Skip(start).Take(i - start).Select(a => a.Id)));
            start = i;
        }
    }

    static string TitlePlaceholder(string title, string type) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Title 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"{type}\"/></p:nvPr></p:nvSpPr>" +
        $"<p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\" dirty=\"0\"/><a:t>{Xml.X(title)}</a:t></a:r></a:p></p:txBody></p:sp>";

    static string TextPlaceholder(string text, string type, uint idx) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Text 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"{type}\" idx=\"{idx}\"/></p:nvPr></p:nvSpPr>" +
        $"<p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\" dirty=\"0\"/><a:t>{Xml.X(text)}</a:t></a:r></a:p></p:txBody></p:sp>";

    /// <summary>A section header on the content layout (the template has no section layout): its description in the middle of the content area.</summary>
    static string SubtitleBox(string text, LayoutInfo layout)
    {
        var c = layout.Content;
        var h = Emu.Inches(0.8);
        var r = new Rect(c.X, c.CenterY - h / 2, c.W, h);
        var t = PlanText.Simple(text, 24, new ThemeColor(SchemeSlot.Tx2), align: HAlign.Center);
        return $"<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Text 2\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr>{Xml.Xfrm(r)}<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>" +
               $"{SlideWriter.TextBody(t)}</p:sp>";
    }

    /// <summary>A layout without a title placeholder: the title goes in a text box above the content area.</summary>
    static string TitleBox(string title, LayoutInfo layout)
    {
        var c = layout.Content;
        var h = Emu.Inches(0.6);
        var r = new Rect(c.X, Math.Max(Emu.Inches(0.2), c.Y - h - Emu.Inches(0.1)), c.W, h);
        var text = PlanText.Simple(title, 24, new ThemeColor(SchemeSlot.Tx1)) with { Font = FontRole.Major, Anchor = VAnchor.Bottom };
        return $"<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Title 1\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr>{Xml.Xfrm(r)}<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>" +
               $"{SlideWriter.TextBody(text)}</p:sp>";
    }

    static IEnumerable<string> Validate(PresentationDocument doc) =>
        new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(doc).Select(e => $"{e.Part?.Uri}: {e.Description}");

    static void Feed(OpenXmlPart part, string xml)
    {
        using var s = new MemoryStream(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml));
        part.FeedData(s);
    }
}
