using Adtd.Model;

namespace Adtd.Layout;

/// <summary>The forest at a glance: a table of facts and a table of domains.</summary>
internal static class Summary
{
    public static SlidePlan Plan(Ctx c)
    {
        var s = c.F;
        var area = c.Content;
        var gap = Emu.Inches(0.4);
        var leftW = (area.W - gap) * 0.45;
        var left = new Rect(area.X, area.Y, leftW, area.H);
        var right = new Rect(area.X + leftW + gap, area.Y, area.W - leftW - gap, area.H);

        var dcs = s.DomainControllers;
        var emptySites = s.Sites.Count(x => c.DcsIn(x.Name).Count == 0);
        var unassigned = s.Subnets.Count(x => x.Site == null);
        var facts = new List<IReadOnlyList<string>>
        {
            new[] { "Forest functional level", FunctionalLevels.Name(s.ForestFunctionalLevel) },
            new[] { "Schema version", $"{FunctionalLevels.SchemaName(s.SchemaVersion)} ({s.SchemaVersion})" },
            new[] { "Schema master", c.DcName(s.SchemaMaster) },
            new[] { "Domain naming master", c.DcName(s.DomainNamingMaster) },
            new[] { "Domains", s.Domains.Count.ToString() },
            new[] { "Domain controllers", $"{dcs.Count} ({dcs.Count(d => d.IsGlobalCatalog)} GC, {dcs.Count(d => d.IsReadOnly)} read-only)" },
            new[] { "Sites", emptySites > 0 ? $"{s.Sites.Count} ({emptySites} without DCs)" : s.Sites.Count.ToString() },
            new[] { "Subnets", unassigned > 0 ? $"{s.Subnets.Count} ({unassigned} not in a site)" : s.Subnets.Count.ToString() },
            new[] { "Site links", s.SiteLinkBridges.Count > 0 ? $"{s.SiteLinks.Count} (and {Ctx.Count(s.SiteLinkBridges.Count, "bridge", "bridges")})" : s.SiteLinks.Count.ToString() },
            new[] { "Replication connections", s.Connections.Count.ToString() },
            new[] { "Collected", $"{s.CollectedAt:yyyy-MM-dd HH:mm} from {s.CollectedFrom}" },
        };
        if (s.Warnings.Count > 0) facts.Add(["Collection warnings", s.Warnings.Count.ToString()]);

        var domains = c.Domains.Select(d => (IReadOnlyList<string>)new[]
        {
            d.Reachable ? d.DnsName : $"{d.DnsName} (not reachable)", d.NetBiosName, FunctionalLevels.Name(d.FunctionalLevel), c.DcsOf(d.DnsName).Count.ToString(),
        }).ToList();
        var cap = Parts.RowCapacity(c, area.H);
        if (domains.Count > cap)
            domains = [.. domains.Take(cap - 1), new[] { $"… and {domains.Count - cap + 1} more", "", "", "" }];
        string[] header = ["Domain", "NetBIOS", "Functional level", "DCs"];
        string[] factsHeader = ["Forest", s.ForestName];

        // Both tables at the same size: the smaller of the sizes each fits at.
        var sizes = c.Ladder(14, 12, 11, 10, 9);
        var t1 = Parts.Table(c, "Forest facts", factsHeader, facts, left, sizes, growth: 1, minWidth: left.W);
        var t2 = Parts.Table(c, "Domains", header, domains, right, sizes, growth: 1, minWidth: right.W);
        var f = Math.Min(t1.FontPt, t2.FontPt);
        var common = sizes.Where(x => x <= f).ToList();
        t1 = Parts.Table(c, "Forest facts", factsHeader, facts, left, common, growth: 1, minWidth: left.W);
        t2 = Parts.Table(c, "Domains", header, domains, right, common, growth: 1, minWidth: right.W);
        return new SlidePlan($"Forest: {s.ForestName}", [t1, t2]);
    }
}
