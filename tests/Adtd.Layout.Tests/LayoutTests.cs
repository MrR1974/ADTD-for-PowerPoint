using Adtd.Model;
using Adtd.Model.Samples;

namespace Adtd.Layout.Tests;

/// <summary>Content areas of the shapes the M0/M2 templates produced, and the forests to lay out on them.</summary>
public static class Fixtures
{
    const double In = Emu.PerInch;

    public static readonly Dictionary<string, SlideFrame> Frames = new()
    {
        // A corporate-style layout: a narrow title in a header bar, content in the master body below it.
        ["wide"] = new(13.333 * In, 7.5 * In, new Rect(0.43 * In, 1.14 * In, 12.48 * In, 5.44 * In), new Rect(0.35 * In, 0.1 * In, 2.85 * In, 0.6 * In)),
        // Office 16:9 Title Only: the master body, inset.
        ["office"] = new(13.333 * In, 7.5 * In, new Rect(1.017 * In, 2.097 * In, 11.3 * In, 4.56 * In), new Rect(0.917 * In, 0.4 * In, 11.5 * In, 1.45 * In)),
        ["4x3"] = new(10 * In, 7.5 * In, new Rect(0.6 * In, 1.6 * In, 8.8 * In, 5.4 * In), new Rect(0.6 * In, 0.3 * In, 8.8 * In, 1.1 * In)),
        // Pitchbook: title down the right-hand side, content almost full height.
        ["side-title"] = new(10 * In, 7.5 * In, new Rect(0.5 * In, 0.5 * In, 8.6 * In, 6.5 * In), new Rect(9.3 * In, 0.4 * In, 0.6 * In, 6.7 * In)),
    };

    /// <summary>A small forest of a common shape: one DC holding every role in a hub site, four spoke sites with no DCs.</summary>
    public static ForestSnapshot HubAndSpokes() => SampleForests.Hub();

    public static ForestSnapshot Forest(string name) => SampleForests.ByName(name);

    public static DeckPlan Plan(ForestSnapshot f, string frame = "wide", DeckOptions? options = null, ThemeInfo? theme = null) =>
        DeckPlanner.Plan(f, Frames[frame], theme ?? ThemeInfo.OfficeDefault, ApproxTextMeasurer.Instance, 112.0 / 139, options);

    public static IEnumerable<PlanItem> All(this SlidePlan s) => s.Items.SelectMany(Flatten);
    static IEnumerable<PlanItem> Flatten(PlanItem i) => i is GroupItem g ? g.Children.SelectMany(Flatten).Prepend(g) : [i];
}

public class InvariantTests
{
    public static IEnumerable<object[]> Cases() =>
        from forest in new[] { "hub", "single", "contoso", "large", "satellites", "regional" }
        from frame in Fixtures.Frames.Keys
        select new object[] { forest, frame };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_slide_keeps_inside_the_content_area_without_overlaps_small_text_or_loose_connectors(string forest, string frame)
    {
        var plan = Fixtures.Plan(Fixtures.Forest(forest), frame);
        var problems = PlanChecks.Check(plan, Fixtures.Frames[frame], ApproxTextMeasurer.Instance, 8);
        Assert.Empty(problems);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("don't fit"));
    }

    [Theory]
    [InlineData("contoso")]
    [InlineData("single")]
    public void Invariants_hold_with_every_option_changed(string forest)
    {
        var options = new DeckOptions
        {
            Replication = true, Addresses = false, Subnets = false, LinkLabels = false, OtherTrusts = false, HideEmptySites = true,
            Legend = false, MaxDcsPerSlide = 4, MinFontPt = 9,
        };
        var plan = Fixtures.Plan(Fixtures.Forest(forest), "4x3", options);
        Assert.Empty(PlanChecks.Check(plan, Fixtures.Frames["4x3"], ApproxTextMeasurer.Instance, 9));
    }

    [Fact]
    public void The_checks_catch_what_they_check()
    {
        var frame = Fixtures.Frames["wide"];
        var c = frame.Content;
        var tx = new ThemeColor(SchemeSlot.Tx1);
        var bad = new DeckPlan([new SlidePlan("Bad",
        [
            new BoxItem("A", new Rect(c.X, c.Y, Emu.Inches(1), Emu.Inches(1)), Geometry.Rect) { Key = "a" },
            new BoxItem("B", new Rect(c.X + Emu.Inches(0.5), c.Y, Emu.Inches(1), Emu.Inches(1)), Geometry.Rect),
            new TextItem("Tiny", new Rect(c.X, c.Bottom - Emu.Inches(0.5), Emu.Inches(3), Emu.Inches(0.4)), PlanText.Simple("small", 6, tx)),
            new TextItem("Long", new Rect(c.X + Emu.Inches(4), c.Y, Emu.Inches(0.3), Emu.Inches(0.4)), PlanText.Simple("far too long for this", 10, tx)),
            new BoxItem("Out", new Rect(c.Right - Emu.Inches(0.5), c.Y + Emu.Inches(2), Emu.Inches(1), Emu.Inches(1)), Geometry.Rect),
            new LinkItem("L", "a", "nowhere", [new Point(c.X, c.Y), new Point(c.X + 1, c.Y)], new Stroke(tx, 1)),
        ])]);
        var problems = PlanChecks.Check(bad, frame, ApproxTextMeasurer.Instance, 8);
        Assert.Contains(problems, p => p.Contains("A overlaps B"));
        Assert.Contains(problems, p => p.Contains("6 pt text"));
        Assert.Contains(problems, p => p.Contains("wider than its box"));
        Assert.Contains(problems, p => p.Contains("Out") && p.Contains("outside"));
        Assert.Contains(problems, p => p.Contains("nowhere"));
    }
}

public class DeckTests
{
    [Fact]
    public void Puts_the_forest_domains_and_trusts_in_one_section_ahead_of_the_sites_each_opened_by_a_header()
    {
        var f = SampleForests.Contoso();
        var slides = Fixtures.Plan(f).Slides;
        var titles = slides.Select(s => s.Title).ToList();
        // A section header opens the forest, domains and trusts.
        Assert.Equal(SlideKind.SectionHeader, slides[0].Kind);
        Assert.Equal("Forest, domains and trusts", titles[0]);
        // Shortcut (both ways, counted once), forest, external and realm trusts, and three parent-child or tree-root trusts.
        Assert.Equal("contoso.com · 4 domains · 7 trusts", slides[0].Subtitle);
        Assert.Empty(slides[0].Items);
        string[] forest = ["Forest: contoso.com", "Domains and trusts", "Domain: contoso.com", "Domain: apac.contoso.com", "Domain: emea.contoso.com", "Domain: fabrikam.com"];
        Assert.Equal(forest, titles.Skip(1).Take(6));
        // And one opens the sites.
        Assert.Equal(SlideKind.SectionHeader, slides[7].Kind);
        Assert.Equal("Sites", titles[7]);
        Assert.Equal($"{f.Sites.Count} sites · {f.SiteLinks.Count} site links", slides[7].Subtitle);
        Assert.Empty(slides[7].Items);
        Assert.Equal("Sites and site links", titles[8]);
        Assert.Equal(f.Sites.Count, titles.Skip(9).Count(t => t.StartsWith("Site:")));
        Assert.Equal(2, slides.Count(s => s.Kind == SlideKind.SectionHeader));
        Assert.All(slides.Take(7), s => Assert.Equal(DeckPlanner.ForestSection, s.Section));
        Assert.All(slides.Skip(7), s => Assert.Equal(DeckPlanner.SitesSection, s.Section));
    }

    [Fact]
    public void Each_section_header_can_be_turned_off_and_a_deck_of_one_section_has_none()
    {
        var f = SampleForests.Contoso();
        var noForest = Fixtures.Plan(f, options: new DeckOptions { ForestSectionHeader = false }).Slides;
        Assert.Equal(["Sites"], noForest.Where(s => s.Kind == SlideKind.SectionHeader).Select(s => s.Title));
        var noSites = Fixtures.Plan(f, options: new DeckOptions { SitesSectionHeader = false }).Slides;
        Assert.Equal(["Forest, domains and trusts"], noSites.Where(s => s.Kind == SlideKind.SectionHeader).Select(s => s.Title));
        var sitesOnly = Fixtures.Plan(f, options: new DeckOptions { Summary = false, DomainsOverview = false, DomainDetails = false }).Slides;
        Assert.DoesNotContain(sitesOnly, s => s.Kind == SlideKind.SectionHeader);
    }

    [Fact]
    public void Domains_and_external_domains_are_solid_rounded_rectangles_like_the_sites()
    {
        var slide = Fixtures.Plan(SampleForests.Contoso()).Slides.Single(s => s.Title == "Domains and trusts");
        var boxes = slide.Items.OfType<BoxItem>().Where(b => b.Key is { } k && (k.StartsWith("dom:") || k.StartsWith("ext:"))).ToList();
        Assert.Contains(boxes, b => b.Key!.StartsWith("ext:"));
        Assert.All(boxes, b =>
        {
            Assert.Equal(Geometry.RoundRect, b.Geometry);
            Assert.NotNull(b.Fill);
            Assert.Equal(Dash.Solid, b.Line!.Dash);
        });
    }

    [Fact]
    public void Slide_types_can_be_turned_off()
    {
        var plan = Fixtures.Plan(SampleForests.Contoso(), options: new DeckOptions { Summary = false, SitesOverview = false, SiteDetails = false });
        Assert.Equal(["Domains and trusts", "Domain: contoso.com", "Domain: apac.contoso.com", "Domain: emea.contoso.com", "Domain: fabrikam.com"],
                     plan.Slides.Select(s => s.Title));
    }

    [Fact]
    public void Empty_sites_are_shown_by_default_with_a_note_and_their_subnets()
    {
        var plan = Fixtures.Plan(Fixtures.HubAndSpokes());
        var site1 = plan.Slides.Single(s => s.Title == "Site: Site1");
        Assert.Contains(site1.Items, i => i is TextItem t && t.Text.Paras[0].Runs[0].Text == "No domain controllers in this site.");
        var table = Assert.Single(site1.Items.OfType<TableItem>());
        Assert.Equal([["10.0.1.0/24"]], table.Rows.Select(r => r.ToArray()));
        var sites = plan.Slides.Single(s => s.Title == "Sites and site links");
        // Every site looks the same: a solid rounded rectangle, with or without DCs.
        var box = sites.Items.OfType<BoxItem>().Single(b => b.Name == "Site Site1");
        var hub = sites.Items.OfType<BoxItem>().Single(b => b.Name == "Site Hub");
        Assert.Equal(Geometry.RoundRect, box.Geometry);
        Assert.Equal(Dash.Solid, box.Line!.Dash);
        Assert.Equal((hub.Line, hub.Fill), (box.Line, box.Fill));
        Assert.Equal("No DCs", box.Text!.Paras[1].Runs[0].Text);

        var hidden = Fixtures.Plan(Fixtures.HubAndSpokes(), options: new DeckOptions { HideEmptySites = true });
        Assert.DoesNotContain(hidden.Slides, s => s.Title == "Site: Site1");
        Assert.DoesNotContain(hidden.Slides.Single(s => s.Title == "Sites and site links").Items, i => i.Name == "Site Site1");
    }

    [Fact]
    public void The_one_DC_in_the_real_forest_shape_carries_every_role_and_the_ISTG_on_its_site_slide()
    {
        var plan = Fixtures.Plan(Fixtures.HubAndSpokes());
        string[] Badges(SlidePlan s) => s.All().OfType<BoxItem>().Where(b => b.Name.StartsWith("CORP-DC01 ") && b.Name != "CORP-DC01 frame")
                                         .Select(b => b.Name["CORP-DC01 ".Length..]).ToArray();
        Assert.Equal(["GC", "PDC", "RID", "Infra", "Schema", "DNM", "ISTG"], Badges(plan.Slides.Single(s => s.Title == "Site: Hub")));
        Assert.Equal(["GC", "PDC", "RID", "Infra", "Schema", "DNM"], Badges(plan.Slides.Single(s => s.Title == "Domain: corp.example")));
    }

    [Fact]
    public void A_site_with_more_DCs_than_fit_continues_on_cont_slides()
    {
        var b = new ForestBuilder("big.example", 7, 88);
        b.Domain("big.example", "BIG", 7);
        b.Site("Main");
        for (var i = 0; i < 90; i++) b.Dc($"DC{i:00}", "big.example", "Main", gc: true, os: "Windows Server 2022 Datacenter");
        for (var i = 0; i < 70; i++) b.Subnet($"10.{i}.0.0/16", "Main", "Somewhere");
        var plan = Fixtures.Plan(b.Build(), options: new DeckOptions { MaxDcsPerSlide = 40 });
        var site = plan.Slides.Where(s => s.Title.StartsWith("Site: Main")).ToList();
        Assert.True(site.Count >= 3);
        Assert.Equal("Site: Main", site[0].Title);
        Assert.All(site.Skip(1), s => Assert.Equal("Site: Main (cont.)", s.Title));
        var cards = site.Select(s => s.Items.OfType<GroupItem>().Count(g => g.Name.StartsWith("DC "))).ToList();
        Assert.Equal(90, cards.Sum());
        Assert.All(cards, n => Assert.True(n <= 40));
        Assert.Equal(70, site.SelectMany(s => s.Items.OfType<TableItem>()).Sum(t => t.Rows.Count));
        Assert.Empty(PlanChecks.Check(plan, Fixtures.Frames["wide"], ApproxTextMeasurer.Instance, 8));
    }

    [Fact]
    public void A_crowded_sites_slide_splits_by_cluster_and_keeps_every_site_once()
    {
        var f = SampleForests.Large();
        var slides = Fixtures.Plan(f, options: new DeckOptions { Summary = false, DomainsOverview = false, DomainDetails = false, SiteDetails = false }).Slides;
        Assert.True(slides.Count > 1);
        Assert.All(slides.Select((s, i) => (s, i)), x => Assert.Equal($"Sites and site links ({x.i + 1}/{slides.Count})", x.s.Title));
        var shown = slides.SelectMany(s => s.Items.OfType<BoxItem>().Where(b => b.Key?.StartsWith("site:") == true).Select(b => b.Key![5..])).ToList();
        Assert.Equal(f.Sites.Select(s => s.Name).Order(), shown.Order());
        // The backbone hub appears on every slide that holds one of its sites.
        Assert.All(slides, s => Assert.Contains(s.Items, i => i is BoxItem { Key: "hub:Backbone" }));
    }

    [Fact]
    public void A_hub_with_too_many_spokes_for_one_slide_is_repeated_on_each_slide_with_every_link_drawn()
    {
        var f = SampleForests.Spokes(80);
        var plan = Fixtures.Plan(f, options: new DeckOptions { Summary = false, DomainsOverview = false, DomainDetails = false, SiteDetails = false });
        var slides = plan.Slides;
        Assert.True(slides.Count > 1);
        Assert.Contains(plan.Warnings, w => w.Contains("repeating Hub-DC"));
        Assert.All(slides, s => Assert.Contains(s.Items, i => i is BoxItem { Key: "site:Hub-DC" }));
        var branches = slides.SelectMany(s => s.Items.OfType<BoxItem>().Select(b => b.Key).Where(k => k?.StartsWith("site:Branch") == true)).ToList();
        Assert.Equal(f.Sites.Where(s => s.Name != "Hub-DC").Select(s => "site:" + s.Name).Order(), branches.Order());
        Assert.Equal(f.SiteLinks.Count, slides.Sum(s => s.Items.OfType<LinkItem>().Count(l => !l.ToKey.StartsWith("away:"))));
    }

    [Fact]
    public void A_repeated_hub_has_a_placeholder_for_the_spokes_on_its_other_slides()
    {
        var f = SampleForests.Spokes(80);
        var slides = Fixtures.Plan(f, options: new DeckOptions { Summary = false, DomainsOverview = false, DomainDetails = false, SiteDetails = false }).Slides;
        var n = slides.Count;
        for (var i = 0; i < n; i++)
        {
            var s = slides[i];
            var here = s.Items.OfType<BoxItem>().Count(b => b.Key?.StartsWith("site:Branch") == true);
            var box = s.Items.OfType<BoxItem>().Single(b => b.Key == "away:site:Hub-DC");
            Assert.Equal(Dash.LongDash, box.Line!.Dash);
            var others = Enumerable.Range(1, n).Where(k => k != i + 1).ToList();
            var where = others.Count == 1 ? $"on slide {others[0]}" : others.SequenceEqual(Enumerable.Range(others[0], others.Count)) ? $"on slides {others[0]}–{others[^1]}" : null;
            Assert.Equal($"+{80 - here} more sites", box.Text!.Paras[0].Runs[0].Text);
            if (where != null) Assert.Equal(where, box.Text.Paras[1].Runs[0].Text);
            Assert.Single(s.Items.OfType<LinkItem>(), l => l.FromKey == "site:Hub-DC" && l.ToKey == "away:site:Hub-DC");
            Assert.Contains(s.Items, i => i is TextItem { Name: "Legend Sites on other slides" });
        }
        // A slide that has all the sites needs no placeholder.
        Assert.DoesNotContain(Fixtures.Plan(SampleForests.Contoso()).Slides.SelectMany(s => s.Items), i => i is BoxItem { Key: not null } b && b.Key.StartsWith("away:"));
    }

    [Fact]
    public void A_hub_cluster_is_split_by_the_groups_hanging_off_the_hub_not_site_by_site()
    {
        var f = SampleForests.Regional();
        var slides = Fixtures.Plan(f, options: new DeckOptions { Summary = false, DomainsOverview = false, DomainDetails = false, SiteDetails = false }).Slides;
        Assert.True(slides.Count > 1);
        var europe = slides.Where(s => s.Items.Any(i => i is BoxItem { Key: "site:Europe-Hub" })).ToList();
        // Europe's branches go with Europe's hub, not one by one beside the Americas hub.
        Assert.All(f.Sites.Where(s => s.Name.StartsWith("EU-Branch")),
                   b => Assert.Contains(europe, s => s.Items.Any(i => i is BoxItem bx && bx.Key == "site:" + b.Name)));
        Assert.DoesNotContain(slides, s => s.Title.Contains("don't fit"));
    }
}

public class ConsolidationTests
{
    static DeckOptions Consolidate => new() { ConsolidateEmptySites = true };

    static SlidePlan Overview(DeckPlan plan) => plan.Slides.Single(s => s.Title == "Sites and site links");

    [Fact]
    public void Empty_spokes_of_one_hub_become_one_aggregate_site_joined_by_one_link()
    {
        var plan = Fixtures.Plan(Fixtures.HubAndSpokes(), options: Consolidate);
        var sites = Overview(plan);
        Assert.DoesNotContain(sites.Items, i => i is BoxItem { Key: "site:Site1" });
        var agg = sites.Items.OfType<BoxItem>().Single(b => b.Key?.StartsWith("agg:") == true);
        Assert.Equal(["Aggregate Sites-1", "4 empty sites"], agg.Text!.Paras.Select(p => p.Runs[0].Text));
        Assert.Equal(Geometry.RoundRect, agg.Geometry);
        var link = Assert.Single(sites.Items.OfType<LinkItem>());
        Assert.Equal((agg.Key, "site:Hub"), (link.FromKey, link.ToKey));
        // The links all cost 100 at 15 minutes, so the aggregated link says so.
        var label = sites.Items.OfType<BoxItem>().Single(b => b.Name == "Site link Aggregated SiteLink label");
        Assert.Equal(["Aggregated SiteLink", "100 · 15 min"], label.Text!.Paras.Select(p => p.Runs[0].Text));
        // The empty sites keep their own slides.
        Assert.Contains(plan.Slides, s => s.Title == "Site: Site1");
        // Off by default.
        Assert.Contains(Overview(Fixtures.Plan(Fixtures.HubAndSpokes())).Items, i => i is BoxItem { Key: "site:Site1" });
    }

    [Fact]
    public void Consolidating_brings_a_split_overview_back_to_one_slide()
    {
        var f = SampleForests.Satellites();
        var options = new DeckOptions { Summary = false, DomainsOverview = false, DomainDetails = false, SiteDetails = false };
        Assert.True(Fixtures.Plan(f, options: options).Slides.Count > 1);
        var plan = Fixtures.Plan(f, options: options with { ConsolidateEmptySites = true });
        var slide = Assert.Single(plan.Slides);
        var agg = slide.Items.OfType<BoxItem>().Single(b => b.Key?.StartsWith("agg:") == true);
        Assert.Equal("54 empty sites", agg.Text!.Paras[1].Runs[0].Text);
        // Six satellites have a DC and stay.
        Assert.Equal(7, slide.Items.OfType<BoxItem>().Count(b => b.Key?.StartsWith("site:") == true));
        // Their links differ in cost, so the aggregated link gives a count instead.
        var label = slide.Items.OfType<BoxItem>().Single(b => b.Name == "Site link Aggregated SiteLink label");
        Assert.Equal(["Aggregated SiteLink", "54 links"], label.Text!.Paras.Select(p => p.Runs[0].Text));
    }

    [Fact]
    public void Only_groups_of_two_or_more_empty_sites_with_a_single_neighbour_are_consolidated()
    {
        var plan = Fixtures.Plan(SampleForests.Regional(), options: Consolidate with { Summary = false, DomainsOverview = false, DomainDetails = false, SiteDetails = false });
        var boxes = plan.Slides.SelectMany(s => s.Items.OfType<BoxItem>()).Where(b => b.Key != null).GroupBy(b => b.Key!).ToDictionary(g => g.Key, g => g.First());
        // Linked to two hubs, alone off its hub, or with no link at all: kept.
        Assert.Contains("site:Transit", boxes.Keys);
        Assert.Contains("site:AS-Branch-03", boxes.Keys);
        Assert.Contains("site:Decommissioned", boxes.Keys);
        // The empty branches of the Americas and Europe, and the two empty sites on the backbone link itself.
        Assert.DoesNotContain(boxes.Keys, k => k.StartsWith("site:Cloud-") || k is "site:AM-Branch-04" or "site:EU-Branch-05");
        var aggs = boxes.Values.Where(b => b.Key!.StartsWith("agg:")).Select(b => b.Text!.Paras[1].Runs[0].Text).Order().ToList();
        Assert.Equal(["2 empty sites", "6 empty sites", "8 empty sites"], aggs);
        Assert.Contains(plan.Slides.SelectMany(s => s.Items.OfType<LinkItem>()), l => l.ToKey == "hub:Backbone" && l.FromKey.StartsWith("agg:"));
        Assert.Empty(PlanChecks.Check(plan, Fixtures.Frames["wide"], ApproxTextMeasurer.Instance, 8));
    }

    [Theory]
    [InlineData("satellites")]
    [InlineData("regional")]
    [InlineData("contoso")]
    public void Invariants_hold_when_consolidating(string forest)
    {
        foreach (var frame in Fixtures.Frames.Keys)
            Assert.Empty(PlanChecks.Check(Fixtures.Plan(Fixtures.Forest(forest), frame, Consolidate), Fixtures.Frames[frame], ApproxTextMeasurer.Instance, 8));
    }
}

public class SitesTests
{
    [Fact]
    public void Site_links_are_labelled_with_their_name_cost_and_interval()
    {
        var sites = Fixtures.Plan(Fixtures.HubAndSpokes()).Slides.Single(s => s.Title == "Sites and site links");
        var label = sites.Items.OfType<BoxItem>().Single(b => b.Name == "Site link Site1-Hub label");
        Assert.Equal(["Site1-Hub", "100 · 15 min"], label.Text!.Paras.Select(p => p.Runs[0].Text));
    }

    [Fact]
    public void A_link_joining_more_than_two_sites_is_a_connection_point_labelled_with_its_name()
    {
        var sites = Fixtures.Plan(SampleForests.Contoso()).Slides.Single(s => s.Title == "Sites and site links");
        var point = sites.Items.OfType<BoxItem>().Single(b => b.Key == "hub:Core");
        Assert.Equal(Geometry.Ellipse, point.Geometry);
        Assert.Null(point.Text);
        Assert.Equal(4, sites.Items.OfType<LinkItem>().Count(l => l.ToKey == "hub:Core"));
        Assert.Equal("Core", sites.Items.OfType<BoxItem>().Single(b => b.Name == "Site link Core label").Text!.Paras[0].Runs[0].Text);
    }
}

public class DomainsTests
{
    [Fact]
    public void Draws_the_trees_and_every_kind_of_trust()
    {
        var slide = Fixtures.Plan(SampleForests.Contoso()).Slides.Single(s => s.Title == "Domains and trusts");
        var boxes = slide.Items.OfType<BoxItem>().Where(b => b.Key != null).ToDictionary(b => b.Key!, b => b.Bounds);
        var links = slide.Items.OfType<LinkItem>().ToList();

        // Tidy tree: the parent sits centred over its children, children below.
        Rect root = boxes["dom:contoso.com"], apac = boxes["dom:apac.contoso.com"], emea = boxes["dom:emea.contoso.com"];
        Assert.Equal(root.CenterX, (apac.CenterX + emea.CenterX) / 2, 0);
        Assert.True(apac.Y > root.Bottom);
        Assert.True(boxes["dom:fabrikam.com"].X > emea.Right);

        LinkItem L(string from, string to) => links.Single(l => l.FromKey == from && l.ToKey == to);
        Assert.Equal(4, L("dom:contoso.com", "dom:apac.contoso.com").Route.Count);   // elbow
        Assert.Equal(5, L("dom:contoso.com", "dom:fabrikam.com").Route.Count);       // tree root, over the bus
        var shortcut = links.Single(l => new[] { l.FromKey, l.ToKey }.Order().SequenceEqual(["dom:apac.contoso.com", "dom:emea.contoso.com"]));
        Assert.Equal((Dash.Dash, ArrowEnds.Both), (shortcut.Line.Dash, shortcut.Arrows));
        Assert.Equal(ArrowEnds.End, L("dom:emea.contoso.com", "ext:partner.example").Arrows);    // outbound: emea trusts partner
        Assert.Equal(ArrowEnds.Start, L("dom:fabrikam.com", "ext:UNIX.EXAMPLE.COM").Arrows);    // inbound
        Assert.Equal(ArrowEnds.Both, L("dom:contoso.com", "ext:tailspin.example").Arrows);
        // Outside the forest: a column on the right.
        Assert.All(boxes.Where(b => b.Key.StartsWith("ext:")), b => Assert.True(b.Value.X > boxes["dom:fabrikam.com"].Right));
    }

    [Fact]
    public void Other_trusts_can_be_left_out()
    {
        var slide = Fixtures.Plan(SampleForests.Contoso(), options: new DeckOptions { OtherTrusts = false }).Slides.Single(s => s.Title == "Domains and trusts");
        Assert.DoesNotContain(slide.Items, i => i is BoxItem { Key: not null } b && b.Key.StartsWith("ext:"));
        Assert.All(slide.Items.OfType<LinkItem>(), l => Assert.Equal(Dash.Solid, l.Line.Dash));
    }
}

public class CardTests
{
    [Fact]
    public void Badges_follow_the_fixed_order_and_FSMO_holders()
    {
        var slide = Fixtures.Plan(SampleForests.Contoso()).Slides.Single(s => s.Title == "Domain: contoso.com");
        string[] Badges(string dc) => slide.All().OfType<BoxItem>().Where(b => b.Name.StartsWith(dc + " ") && b.Text != null).Select(b => b.Name[(dc.Length + 1)..]).ToArray();
        Assert.Equal(["GC", "PDC", "Schema", "DNM"], Badges("SYD-DC01"));
        Assert.Equal(["GC", "RID", "Infra"], Badges("SYD-DC02"));
        var apac = Fixtures.Plan(SampleForests.Contoso()).Slides.Single(s => s.Title == "Domain: apac.contoso.com");
        var rodc = apac.All().OfType<BoxItem>().Single(b => b.Name == "BNE-AP04 frame");
        Assert.Equal(Dash.Dash, rodc.Line!.Dash);
    }

    [Fact]
    public void Shows_two_addresses_then_a_count()
    {
        var b = new ForestBuilder("a.example", 7, 88);
        b.Domain("a.example", "A", 7);
        b.Site("S");
        b.Dc("MANY", "a.example", "S", gc: false, ips: ["10.0.0.1", "10.0.0.2", "10.0.0.3", "10.0.0.4"]);
        b.Dc("NONE", "a.example", "S", gc: false, ips: []);
        var slide = Fixtures.Plan(b.Build()).Slides.Single(s => s.Title == "Site: S");
        string Line(string dc) => slide.All().OfType<TextItem>().Single(t => t.Name == dc + " details").Text.Paras[1].Runs[0].Text;
        Assert.Equal("10.0.0.1, 10.0.0.2 +2", Line("MANY"));
        Assert.Equal("No IPv4 address", Line("NONE"));
    }

    [Fact]
    public void Cards_on_a_slide_are_all_the_same_size()
    {
        var slide = Fixtures.Plan(SampleForests.Contoso()).Slides.Single(s => s.Title == "Site: Sydney");
        var frames = slide.All().OfType<BoxItem>().Where(b => b.Name.EndsWith(" frame") && b.Key != null).Select(b => (b.Bounds.W, b.Bounds.H)).Distinct().ToList();
        Assert.Single(frames);
    }

    [Fact]
    public void Chunks_are_as_even_as_possible()
    {
        Assert.Equal([4, 3, 3], Grid.Chunk(Enumerable.Range(0, 10).ToList(), 4).Select(c => c.Count));
        Assert.Equal([5], Grid.Chunk(Enumerable.Range(0, 5).ToList(), 5).Select(c => c.Count));
    }
}

public class ColorTests
{
    /// <summary>A made-up corporate palette: a near-black first accent, two similar dark blues, a dark green and two bright accents.</summary>
    static readonly ThemeInfo Corporate = new("Corporate", new Dictionary<SchemeSlot, string>
    {
        [SchemeSlot.Tx1] = "000000", [SchemeSlot.Bg1] = "FFFFFF", [SchemeSlot.Tx2] = "5B2C9F", [SchemeSlot.Bg2] = "E7E6E6",
        [SchemeSlot.Accent1] = "1F262D", [SchemeSlot.Accent2] = "1A4D6B", [SchemeSlot.Accent3] = "4F8A6E",
        [SchemeSlot.Accent4] = "2C7389", [SchemeSlot.Accent5] = "F2B705", [SchemeSlot.Accent6] = "F07C1A", [SchemeSlot.Hlink] = "0563C1",
    }, "Aptos", "Aptos");

    [Fact]
    public void Starts_with_accent_1_and_keeps_neighbours_apart()
    {
        var (order, warnings) = ColorAssigner.SlotOrder(Corporate);
        Assert.Empty(warnings);
        Assert.Equal(SchemeSlot.Accent1, order[0]);
        Assert.Equal(6, order.Distinct().Count());
        // The two bright accents aren't next to each other, nor are the near-identical dark blues.
        int At(SchemeSlot s) => order.ToList().IndexOf(s);
        Assert.NotEqual(1, Math.Abs(At(SchemeSlot.Accent5) - At(SchemeSlot.Accent6)));
        Assert.NotEqual(1, Math.Abs(At(SchemeSlot.Accent2) - At(SchemeSlot.Accent4)));
    }

    [Fact]
    public void Cycles_with_variants_after_six_and_is_stable()
    {
        var names = Enumerable.Range(0, 13).Select(i => $"d{i}.example").ToList();
        var a = new ColorAssigner(names, Corporate);
        var colours = names.Select(a.Of).ToList();
        Assert.Equal(13, colours.Distinct().Count());
        Assert.Equal(colours[0].Slot, colours[6].Slot);
        Assert.NotEqual(colours[0], colours[6]);
        Assert.Equal(colours, names.Select(new ColorAssigner(names, Corporate).Of));
        Assert.NotEqual(colours[0], a.Of("unknown.example"));
    }

    [Fact]
    public void Warns_when_the_accents_are_all_alike()
    {
        var grey = Corporate with { Colors = Corporate.Colors.ToDictionary(kv => kv.Key, kv => kv.Key >= SchemeSlot.Accent1 && kv.Key <= SchemeSlot.Accent6 ? "606060" : kv.Value) };
        Assert.NotEmpty(ColorAssigner.SlotOrder(grey).Warnings);
    }
}

public class TextTests
{
    readonly ITextMeasurer m = ApproxTextMeasurer.Instance;

    [Fact]
    public void Truncates_with_an_ellipsis_to_fit()
    {
        var w = m.Width("Head office", 10);
        Assert.Equal("Head office", m.Truncate("Head office", w, 10));
        var cut = m.Truncate("Head office and data centre", w, 10);
        Assert.EndsWith("…", cut);
        Assert.True(m.Width(cut, 10) <= w);
    }

    [Fact]
    public void Wraps_between_segments_only()
    {
        string[] segs = ["one", "two", "three"];
        var w = m.Width("one · two", 10) + 1;
        Assert.Equal(["one · two", "three"], m.Wrap(segs, " · ", w, 10, 2));
        Assert.Null(m.Wrap(segs, " · ", m.Width("three", 10) + 1, 10, 2));
    }
}

public class WebLayoutTests
{
    static double D(Point p, Point q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));

    [Fact]
    public void Is_deterministic_and_keeps_nodes_apart()
    {
        // A hub with seven spokes, one spoke with three of its own, and a connection point (5 × 5) joining three spokes.
        var sizes = Enumerable.Repeat((30.0, 12.0), 11).Append((5.0, 5.0)).ToList();
        (int, int)[] edges = [(0, 1), (0, 2), (0, 3), (0, 4), (0, 5), (0, 6), (0, 7), (4, 8), (4, 9), (4, 10), (1, 11), (2, 11), (3, 11)];
        var a = WebLayout.Arrange(sizes, edges);
        Assert.Equal(a, WebLayout.Arrange(sizes, edges));
        for (var i = 0; i < 11; i++)
            for (var j = i + 1; j < 11; j++)
                Assert.True(D(a[i], a[j]) > 20, $"{i} and {j} are {D(a[i], a[j]):0} mm apart");
        // CenterLayout: the layout starts at the origin.
        Assert.Equal(0, Enumerable.Range(0, 12).Min(i => a[i].X - sizes[i].Item1 / 2), 6);
        Assert.Equal(0, Enumerable.Range(0, 12).Min(i => a[i].Y - sizes[i].Item2 / 2), 6);
    }

    [Fact]
    public void Puts_unconnected_nodes_in_a_column_to_the_right()
    {
        var sizes = Enumerable.Repeat((30.0, 12.0), 5).ToList();
        var a = WebLayout.Arrange(sizes, [(0, 1), (0, 2)]);
        var right = new[] { 0, 1, 2 }.Max(i => a[i].X + 15);
        Assert.All(new[] { 3, 4 }, i => Assert.Equal(right + 50, a[i].X, 6));
        Assert.True(a[4].Y < a[3].Y);   // stacked upwards from the bottom, as ADTD.Net did
    }
}
