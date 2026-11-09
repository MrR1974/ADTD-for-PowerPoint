namespace Adtd.Model.Samples;

/// <summary>
/// Made-up forests for tests, layout tuning and demos. Deterministic: the same call always returns the same forest.
/// All names use reserved example domains (contoso, fabrikam, example).
/// </summary>
public static class SampleForests
{
    public static readonly string[] Names = ["single", "contoso", "hub", "satellites", "regional", "spokes", "large"];

    /// <summary>What each sample is for, in a line.</summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["single"] = "one domain, two sites",
        ["contoso"] = "two trees, four domains, every kind of trust, 18 sites",
        ["hub"] = "one DC in a hub site with four empty spoke sites",
        ["satellites"] = "a hub with 60 satellite sites, most of them empty",
        ["regional"] = "three regions of populated and empty sites, a backbone link and a forest trust",
        ["spokes"] = "a hub with 80 branch sites, each with a read-only DC",
        ["large"] = "three domains, 150 sites, 400 DCs",
    };

    public static ForestSnapshot ByName(string name) => name.ToLowerInvariant() switch
    {
        "single" => Single(),
        "contoso" => Contoso(),
        "hub" => Hub(),
        "satellites" => Satellites(),
        "regional" => Regional(),
        "large" => Large(),
        "spokes" => Spokes(),
        _ => throw new ArgumentException($"Unknown sample forest '{name}'. Choose one of: {string.Join(", ", Names)}."),
    };

    /// <summary>One domain, two sites, three DCs (one a read-only DC).</summary>
    public static ForestSnapshot Single()
    {
        var b = new ForestBuilder("corp.example", 7, 88);
        b.Domain("corp.example", "CORP", 7);
        b.Site("Head-Office", "Sydney", "Head office and data centre");
        b.Site("Branch", "Newcastle", options: 0x20);
        b.Dc("HQ-DC01", "corp.example", "Head-Office", gc: true, os: Os2022);
        b.Dc("HQ-DC02", "corp.example", "Head-Office", gc: true, os: Os2019);
        b.Dc("BR-RODC01", "corp.example", "Branch", gc: true, rodc: true, os: Os2022);
        b.Subnet("10.1.0.0/16", "Head-Office", "Sydney");
        b.Subnet("10.2.0.0/24", "Branch", "Newcastle");
        b.Subnet("10.2.1.0/24", "Branch", "Newcastle", "Wi-Fi");
        b.Link("Head-Office-Branch", ["Head-Office", "Branch"], 100, 60);
        return b.Build();
    }

    /// <summary>
    /// Two trees and four domains, external, forest and realm trusts, 18 sites with a four-site hub link,
    /// read-only DCs, a site with no DCs and a site with no subnets.
    /// </summary>
    public static ForestSnapshot Contoso()
    {
        var b = new ForestBuilder("contoso.com", 7, 88);
        b.Domain("contoso.com", "CONTOSO", 7);
        b.Domain("emea.contoso.com", "EMEA", 7);
        b.Domain("apac.contoso.com", "APAC", 7);
        b.Domain("fabrikam.com", "FABRIKAM", 6);

        (string Name, string Location)[] sites =
        [
            ("Sydney", "Sydney, AU"), ("London", "London, UK"), ("New-York", "New York, US"), ("Singapore", "Singapore"),
            ("Melbourne", "Melbourne, AU"), ("Brisbane", "Brisbane, AU"), ("Perth", "Perth, AU"), ("Auckland", "Auckland, NZ"),
            ("Tokyo", "Tokyo, JP"), ("Paris", "Paris, FR"), ("Frankfurt", "Frankfurt, DE"), ("Dublin", "Dublin, IE"),
            ("Johannesburg", "Johannesburg, ZA"), ("Madrid", "Madrid, ES"), ("Chicago", "Chicago, US"), ("Toronto", "Toronto, CA"),
            ("Seattle", "Seattle, US"), ("Azure-AustraliaEast", "Azure Australia East"),
        ];
        foreach (var (name, loc) in sites)
            b.Site(name, loc, options: name is "Brisbane" or "Perth" or "Dublin" or "Toronto" ? 0x20 : 0);

        b.Dc("SYD-DC01", "contoso.com", "Sydney", gc: true, os: Os2022);
        b.Dc("SYD-DC02", "contoso.com", "Sydney", gc: true, os: Os2022);
        b.Dc("SIN-AP01", "apac.contoso.com", "Singapore", gc: true, os: Os2022);
        b.Dc("SYD-AP02", "apac.contoso.com", "Sydney", gc: false, os: Os2019);
        b.Dc("LON-EM01", "emea.contoso.com", "London", gc: true, os: Os2022);
        b.Dc("LON-DC03", "contoso.com", "London", gc: true, os: Os2022);
        b.Dc("LON-EM02", "emea.contoso.com", "London", gc: false, os: Os2019);
        b.Dc("NYC-FB01", "fabrikam.com", "New-York", gc: true, os: Os2016);
        b.Dc("NYC-DC04", "contoso.com", "New-York", gc: true, os: Os2022);
        b.Dc("NYC-FB02", "fabrikam.com", "New-York", gc: false, os: Os2016);
        b.Dc("SIN-AP03", "apac.contoso.com", "Singapore", gc: true, os: Os2025);
        b.Dc("MEL-DC05", "contoso.com", "Melbourne", gc: true, os: Os2022);
        b.Dc("BNE-AP04", "apac.contoso.com", "Brisbane", gc: true, rodc: true, os: Os2022);
        b.Dc("PER-AP05", "apac.contoso.com", "Perth", gc: true, rodc: true, os: Os2022);
        b.Dc("AKL-AP06", "apac.contoso.com", "Auckland", gc: true, os: Os2019);
        b.Dc("TYO-AP07", "apac.contoso.com", "Tokyo", gc: true, os: Os2025);
        b.Dc("PAR-EM03", "emea.contoso.com", "Paris", gc: true, os: Os2019);
        b.Dc("FRA-EM04", "emea.contoso.com", "Frankfurt", gc: true, os: Os2022);
        b.Dc("FRA-EM05", "emea.contoso.com", "Frankfurt", gc: false, os: Os2022);
        b.Dc("DUB-EM06", "emea.contoso.com", "Dublin", gc: true, rodc: true, os: Os2022);
        b.Dc("JNB-EM07", "emea.contoso.com", "Johannesburg", gc: true, os: Os2016);
        b.Dc("CHI-DC06", "contoso.com", "Chicago", gc: true, os: Os2019);
        b.Dc("CHI-FB03", "fabrikam.com", "Chicago", gc: true, os: Os2016);
        b.Dc("TOR-FB04", "fabrikam.com", "Toronto", gc: true, rodc: true, os: Os2019);
        b.Dc("SEA-DC07", "contoso.com", "Seattle", gc: true, os: Os2022, ips: []);
        b.Dc("AZ-DC08", "contoso.com", "Azure-AustraliaEast", gc: true, os: Os2025);
        b.Dc("AZ-EM08", "emea.contoso.com", "Azure-AustraliaEast", gc: true, os: Os2025);
        b.Dc("AZ-AP08", "apac.contoso.com", "Azure-AustraliaEast", gc: true, os: Os2025);
        b.Dc("AZ-FB05", "fabrikam.com", "Azure-AustraliaEast", gc: true, os: Os2022);
        b.Dc("SYD-DC09", "contoso.com", "Sydney", gc: true, os: Os2025, ips: ["10.0.0.19", "10.0.8.19"]);
        b.Fsmo("contoso.com", pdc: "SYD-DC01", rid: "SYD-DC02", infra: "SYD-DC02");

        for (var i = 0; i < sites.Length; i++)
        {
            var (name, loc) = sites[i];
            if (name == "Seattle") continue;                        // a site with no subnets
            b.Subnet($"10.{i}.0.0/20", name, loc);
            if (i % 3 == 0) b.Subnet($"10.{i}.16.0/22", name, loc, "Servers");
            if (i % 4 == 1) b.Subnet($"172.16.{i}.0/24", name, loc, "Guest Wi-Fi");
        }
        b.Subnet("192.168.250.0/24", null, null, "Lab (unassigned)");

        b.Link("Core", ["Sydney", "London", "New-York", "Singapore"], 100, 15, options: 1);
        foreach (var s in new[] { "Melbourne", "Brisbane", "Perth", "Azure-AustraliaEast" }) b.Link($"Sydney-{s}", ["Sydney", s], 50, 15);
        foreach (var s in new[] { "Auckland", "Tokyo" }) b.Link($"Singapore-{s}", ["Singapore", s], 200, 60);
        foreach (var s in new[] { "Paris", "Frankfurt", "Dublin", "Madrid" }) b.Link($"London-{s}", ["London", s], 100, 30);
        b.Link("London-Johannesburg", ["London", "Johannesburg"], 300, 180, scheduleHours: 120);
        foreach (var s in new[] { "Chicago", "Toronto", "Seattle" }) b.Link($"New-York-{s}", ["New-York", s], 100, 30);
        b.Bridge("APAC-Bridge", ["Core", "Singapore-Tokyo", "Singapore-Auckland"]);

        b.ExtraTrust("emea.contoso.com", new Trust { Partner = "apac.contoso.com", FlatName = "APAC", Direction = TrustDirection.Bidirectional, Kind = TrustKind.Shortcut, TrustType = 2, Attributes = TrustAttributes.WithinForest });
        b.ExtraTrust("apac.contoso.com", new Trust { Partner = "emea.contoso.com", FlatName = "EMEA", Direction = TrustDirection.Bidirectional, Kind = TrustKind.Shortcut, TrustType = 2, Attributes = TrustAttributes.WithinForest });
        b.ExtraTrust("contoso.com", new Trust { Partner = "tailspin.example", FlatName = "TAILSPIN", Direction = TrustDirection.Bidirectional, Kind = TrustKind.Forest, TrustType = 2, Attributes = TrustAttributes.ForestTransitive });
        b.ExtraTrust("emea.contoso.com", new Trust { Partner = "partner.example", FlatName = "PARTNER", Direction = TrustDirection.Outbound, Kind = TrustKind.External, TrustType = 2, Attributes = TrustAttributes.QuarantinedDomain });
        b.ExtraTrust("fabrikam.com", new Trust { Partner = "UNIX.EXAMPLE.COM", Direction = TrustDirection.Inbound, Kind = TrustKind.Realm, TrustType = 3, Attributes = TrustAttributes.NonTransitive });

        b.ManualConnection("Manual LON-SYD", from: "SYD-DC01", to: "LON-DC03");
        b.ManualConnection("Disabled FRA-PAR", from: "FRA-EM04", to: "PAR-EM03", enabled: false);
        return b.Build();
    }

    /// <summary>
    /// A big forest for stress-testing layout: three domains, <paramref name="sites"/> sites in regions of about ten
    /// around a hub, and <paramref name="dcs"/> DCs spread across them.
    /// </summary>
    public static ForestSnapshot Large(int sites = 150, int dcs = 400, int seed = 7)
    {
        var rnd = new Random(seed);
        var b = new ForestBuilder("large.example", 7, 88);
        string[] domains = ["large.example", "east.large.example", "west.large.example"];
        string[] nb = ["LARGE", "EAST", "WEST"];
        for (var i = 0; i < domains.Length; i++) b.Domain(domains[i], nb[i], 7);

        var names = Enumerable.Range(1, sites).Select(i => $"Site-{i:000}").ToList();
        foreach (var n in names) b.Site(n, $"Location {n[5..]}");
        var hubs = names.Where((_, i) => i % 10 == 0).ToList();

        // Every site gets at least one DC (hubs get three); the rest are spread at random.
        var counts = names.Select((n, i) => i % 10 == 0 ? 3 : 1).ToArray();
        for (var extra = dcs - counts.Sum(); extra > 0; extra--) counts[rnd.Next(sites)]++;
        string[] oses = [Os2016, Os2019, Os2022, Os2025];
        var dcNo = 0;
        for (var i = 0; i < sites; i++)
            for (var k = 0; k < counts[i]; k++)
            {
                dcNo++;
                var d = dcNo <= 3 ? dcNo - 1 : rnd.Next(domains.Length);
                var rodc = i % 10 != 0 && k == 0 && rnd.NextDouble() < 0.15;
                b.Dc($"DC{dcNo:0000}", domains[d], names[i], gc: rodc || rnd.NextDouble() < 0.7, rodc: rodc, os: oses[rnd.Next(oses.Length)]);
            }

        for (var i = 0; i < sites; i++)
        {
            b.Subnet($"10.{i % 256}.{i / 256}.0/24", names[i], $"Location {names[i][5..]}");
            for (var extra = rnd.Next(3); extra > 0; extra--) b.Subnet($"172.{16 + extra}.{i % 256}.{i / 256 * 16}/28", names[i], null);
        }

        b.Link("Backbone", hubs, 100, 15, options: 1);
        for (var i = 0; i < sites; i++)
            if (i % 10 != 0) b.Link($"{names[i - i % 10]}-{names[i]}", [names[i - i % 10], names[i]], 100 + rnd.Next(4) * 50, 30);
        return b.Build();
    }

    /// <summary>
    /// One domain, one hub site (the data centre, four DCs and the FSMO roles) and <paramref name="branches"/> branch
    /// sites, each with one read-only DC and its own two-site link to the hub.
    /// </summary>
    public static ForestSnapshot Spokes(int branches = 80)
    {
        var b = new ForestBuilder("retail.example", 7, 88);
        b.Domain("retail.example", "RETAIL", 7);
        b.Site("Hub-DC", "Data centre");
        for (var i = 1; i <= 4; i++) b.Dc($"HUB-DC{i:00}", "retail.example", "Hub-DC", gc: true, os: i <= 2 ? Os2022 : Os2025);
        b.Subnet("10.0.0.0/16", "Hub-DC", "Data centre");
        for (var i = 1; i <= branches; i++)
        {
            var site = $"Branch-{i:000}";
            b.Site(site, $"Store {i:000}");
            b.Dc($"BR{i:000}-RODC", "retail.example", site, gc: true, rodc: true, os: Os2022);
            b.Subnet($"10.{1 + i / 256}.{i % 256}.0/24", site, $"Store {i:000}");
            b.Link($"Hub-DC-{site}", ["Hub-DC", site], 100, 60);
        }
        return b.Build();
    }

    /// <summary>
    /// A small forest of a common shape: one DC holding every role in a hub site, and four spoke sites with no DCs, each
    /// with its own two-site link to the hub.
    /// </summary>
    public static ForestSnapshot Hub()
    {
        var b = new ForestBuilder("corp.example", 10, 91);
        b.Domain("corp.example", "CORP", 10);
        b.Site("Hub");
        for (var i = 1; i <= 4; i++) b.Site($"Site{i}");
        b.Dc("CORP-DC01", "corp.example", "Hub", gc: true, os: "Windows Server 2025 Datacenter Azure Edition");
        b.Fsmo("corp.example", pdc: "CORP-DC01", rid: "CORP-DC01", infra: "CORP-DC01");
        for (var i = 0; i <= 4; i++) b.Subnet($"10.0.{i}.0/24", i == 0 ? "Hub" : $"Site{i}", null);
        for (var i = 1; i <= 4; i++) b.Link($"Site{i}-Hub", ["Hub", $"Site{i}"], 100, 15);
        return b.Build();
    }

    /// <summary>
    /// One hub site and <paramref name="satellites"/> satellite sites, each with its own two-site link to the hub. Every
    /// tenth satellite has a DC; the rest are empty (sites kept for their subnets). Too many to draw on one slide, so the
    /// sites overview splits round the hub, unless the empty sites are consolidated.
    /// </summary>
    public static ForestSnapshot Satellites(int satellites = 60)
    {
        var b = new ForestBuilder("field.example", 10, 91);
        b.Domain("field.example", "FIELD", 10);
        b.Site("HQ", "Head office");
        b.Dc("HQ-DC01", "field.example", "HQ", gc: true, os: Os2025);
        b.Dc("HQ-DC02", "field.example", "HQ", gc: true, os: Os2022);
        b.Subnet("10.0.0.0/16", "HQ", "Head office");
        for (var i = 1; i <= satellites; i++)
        {
            var site = $"Field-{i:00}";
            b.Site(site, $"Field office {i:00}");
            if (i % 10 == 0) b.Dc($"FLD{i:00}-DC01", "field.example", site, gc: true, rodc: true, os: Os2022);
            b.Subnet($"10.{1 + i / 256}.{i % 256}.0/24", site, $"Field office {i:00}");
            // Most share one cost and interval; a few are slower, so not every aggregated link is alike.
            b.Link($"HQ-{site}", ["HQ", site], i % 7 == 0 ? 200 : 100, i % 7 == 0 ? 60 : 30);
        }
        return b.Build();
    }

    /// <summary>
    /// Three domains in two trees and a forest trust; three regional hub sites joined by a three-site backbone link, each
    /// region with populated and empty branch sites. Edge cases for consolidating empty sites: two empty sites that hang
    /// off the backbone link itself, an empty site linked to two hubs (not consolidated), a lone empty branch (not
    /// consolidated: a group needs two), and an empty site with no site link.
    /// </summary>
    public static ForestSnapshot Regional()
    {
        var b = new ForestBuilder("northwind.example", 7, 88);
        b.Domain("northwind.example", "NORTHWIND", 7);
        b.Domain("eu.northwind.example", "NWEU", 7);
        b.Domain("tailspin.example", "TAILSPIN", 7);
        b.ExtraTrust("northwind.example", new Trust { Partner = "wingtip.example", FlatName = "WINGTIP", Direction = TrustDirection.Bidirectional, Kind = TrustKind.Forest, TrustType = 2, Attributes = TrustAttributes.ForestTransitive });

        (string Hub, string Domain, string Prefix, int Populated, int Empty)[] regions =
        [
            ("Americas-Hub", "northwind.example", "AM", 3, 8),
            ("Europe-Hub", "eu.northwind.example", "EU", 4, 6),
            ("Asia-Hub", "tailspin.example", "AS", 2, 1),
        ];
        var net = 0;
        foreach (var (hub, domain, prefix, populated, empty) in regions)
        {
            b.Site(hub, $"{prefix} data centre");
            b.Dc($"{prefix}-DC01", domain, hub, gc: true, os: Os2022);
            b.Dc($"{prefix}-DC02", domain, hub, gc: true, os: Os2025);
            b.Subnet($"10.{net++}.0.0/16", hub, $"{prefix} data centre");
            for (var i = 1; i <= populated + empty; i++)
            {
                var site = $"{prefix}-Branch-{i:00}";
                b.Site(site, $"{prefix} branch {i:00}");
                if (i <= populated) b.Dc($"{prefix}-BR{i:00}", domain, site, gc: true, rodc: i % 2 == 0, os: i % 2 == 0 ? Os2022 : Os2019);
                b.Subnet($"10.{net++}.0.0/24", site, $"{prefix} branch {i:00}");
                b.Link($"{hub}-{site}", [hub, site], 100, i <= populated ? 15 : 60);
            }
        }
        b.Link("Backbone", ["Americas-Hub", "Europe-Hub", "Asia-Hub", "Cloud-Staging", "Cloud-Recovery"], 50, 15, options: 1);
        foreach (var s in new[] { "Cloud-Staging", "Cloud-Recovery" })
        {
            b.Site(s, "Azure");
            b.Subnet($"10.{net++}.0.0/24", s, "Azure");
        }
        b.Site("Transit", "Between the Americas and Europe");
        b.Subnet($"10.{net++}.0.0/24", "Transit", null);
        b.Link("Americas-Hub-Transit", ["Americas-Hub", "Transit"], 150, 30);
        b.Link("Europe-Hub-Transit", ["Europe-Hub", "Transit"], 150, 30);
        b.Site("Decommissioned", "No longer in use");
        return b.Build();
    }

    const string Os2016 = "Windows Server 2016 Standard";
    const string Os2019 = "Windows Server 2019 Standard";
    const string Os2022 = "Windows Server 2022 Datacenter";
    const string Os2025 = "Windows Server 2025 Datacenter";
}
