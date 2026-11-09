using System.Security.Cryptography;
using System.Text;

namespace Adtd.Model.Samples;

/// <summary>
/// Builds a consistent made-up forest: DC keys, FSMO holders, ISTGs, parent-child and tree-root trusts, KCC-style
/// replication connections and DC addresses are filled in the way AD would have them.
/// </summary>
public sealed class ForestBuilder(string forest, int forestLevel, int schemaVersion)
{
    readonly List<Domain> _domains = [];
    readonly List<Site> _sites = [];
    readonly List<DomainController> _dcs = [];
    readonly List<Subnet> _subnets = [];
    readonly List<SiteLink> _links = [];
    readonly List<SiteLinkBridge> _bridges = [];
    readonly List<(string Domain, Trust Trust)> _extraTrusts = [];
    readonly List<ReplicationConnection> _manual = [];
    readonly Dictionary<string, (string? Pdc, string? Rid, string? Infra)> _fsmo = new(StringComparer.OrdinalIgnoreCase);

    public string ConfigNc { get; } = "CN=Configuration," + DnOf(forest);

    public static string DnOf(string dns) => string.Join(',', dns.Split('.').Select(l => "DC=" + l));
    public string DcKey(string name, string site) => $"CN=NTDS Settings,CN={name},CN=Servers,CN={site},CN=Sites,{ConfigNc}";

    public void Domain(string dns, string netBios, int level) =>
        _domains.Add(new Domain { DnsName = dns, NetBiosName = netBios, DistinguishedName = DnOf(dns), FunctionalLevel = level });

    public void Site(string name, string? location = null, string? description = null, int options = 0) =>
        _sites.Add(new Site { Name = name, Location = location, Description = description, Options = options });

    /// <summary>Adds a DC. <paramref name="ips"/> null gives it the next address in its site's range; [] gives it none.</summary>
    public void Dc(string name, string domain, string site, bool gc, bool rodc = false, string? os = null, string[]? ips = null)
    {
        var si = _sites.FindIndex(s => s.Name == site);
        if (si < 0) throw new ArgumentException($"Unknown site {site}");
        var n = _dcs.Count(d => d.Site == site);
        _dcs.Add(new DomainController
        {
            Key = DcKey(name, site),
            Name = name,
            DnsHostName = $"{name.ToLowerInvariant()}.{domain}",
            Domain = domain,
            Site = site,
            IsGlobalCatalog = gc,
            IsReadOnly = rodc,
            OperatingSystem = os,
            OperatingSystemVersion = os == null ? null : OsVersion(os),
            IPv4 = ips ?? [$"10.{si % 256}.{si / 256}.{10 + n}"],
        });
    }

    public void Fsmo(string domain, string pdc, string rid, string infra) => _fsmo[domain] = (pdc, rid, infra);

    public void Subnet(string prefix, string? site, string? location, string? description = null) =>
        _subnets.Add(new Subnet { Prefix = prefix, Site = site, Location = location, Description = description });

    public void Link(string name, IReadOnlyList<string> sites, int cost, int interval, int options = 0, int? scheduleHours = null) =>
        _links.Add(new SiteLink { Name = name, Sites = sites, Cost = cost, ReplicationIntervalMinutes = interval, Options = options, ScheduleHours = scheduleHours });

    public void Bridge(string name, IReadOnlyList<string> links) => _bridges.Add(new SiteLinkBridge { Name = name, SiteLinks = links });

    public void ExtraTrust(string domain, Trust trust) => _extraTrusts.Add((domain, trust));

    public void ManualConnection(string name, string from, string to, bool enabled = true) =>
        _manual.Add(new ReplicationConnection { Name = name, From = Key(from), To = Key(to), Enabled = enabled });

    public ForestSnapshot Build()
    {
        var parents = Trusts.Parents(_domains.Select(d => d.DnsName));
        string? FirstWritable(Func<DomainController, bool> where) => _dcs.FirstOrDefault(d => !d.IsReadOnly && where(d))?.Key;

        var domains = _domains.Select(d =>
        {
            var auto = FirstWritable(dc => dc.Domain == d.DnsName);
            var (pdc, rid, infra) = _fsmo.TryGetValue(d.DnsName, out var f) ? (Key(f.Pdc!), Key(f.Rid!), Key(f.Infra!)) : (auto, auto, auto);
            var trusts = new List<Trust>();
            void In(string partner, TrustKind kind) => trusts.Add(new Trust
            {
                Partner = partner, FlatName = _domains.First(x => x.DnsName == partner).NetBiosName,
                Direction = TrustDirection.Bidirectional, Kind = kind, TrustType = 2, Attributes = TrustAttributes.WithinForest,
            });
            if (parents[d.DnsName] is string parent) In(parent, TrustKind.ParentChild);
            foreach (var child in parents.Where(p => p.Value == d.DnsName).Select(p => p.Key)) In(child, TrustKind.ParentChild);
            if (parents[d.DnsName] == null)
            {
                if (d.DnsName == forest)
                    foreach (var root in parents.Where(p => p.Value == null && p.Key != forest).Select(p => p.Key)) In(root, TrustKind.TreeRoot);
                else In(forest, TrustKind.TreeRoot);
            }
            trusts.AddRange(_extraTrusts.Where(t => t.Domain == d.DnsName).Select(t => t.Trust));
            return d with { ParentDns = parents[d.DnsName], PdcMaster = pdc, RidMaster = rid, InfrastructureMaster = infra, Trusts = trusts };
        }).ToList();

        var root = domains.First(d => d.DnsName == forest);
        var sites = _sites.Select(s => s with
        {
            Istg = FirstWritable(dc => dc.Site == s.Name) ?? _dcs.FirstOrDefault(dc => dc.Site == s.Name)?.Key,
        }).ToList();

        return SnapshotJson.Normalize(new ForestSnapshot
        {
            ForestName = forest,
            CollectedAt = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.FromHours(11)),
            CollectedFrom = "sample",
            CollectorVersion = "sample",
            ForestFunctionalLevel = forestLevel,
            SchemaVersion = schemaVersion,
            SchemaMaster = root.PdcMaster,
            DomainNamingMaster = root.PdcMaster,
            Domains = domains,
            DomainControllers = _dcs,
            Sites = sites,
            Subnets = _subnets,
            SiteLinks = _links,
            SiteLinkBridges = _bridges,
            Connections = Connections(),
        });
    }

    /// <summary>
    /// Roughly what the KCC builds: a two-way ring between the writable DCs of each site, read-only DCs pulling from a
    /// writable DC, and both-way connections between the bridgeheads (first writable DC) of sites sharing a link.
    /// </summary>
    List<ReplicationConnection> Connections()
    {
        var result = new List<ReplicationConnection>();
        var seen = new HashSet<(string, string)>();
        void Add(DomainController from, DomainController to, bool interSite)
        {
            if (from.Key == to.Key || !seen.Add((from.Key, to.Key))) return;
            result.Add(new ReplicationConnection
            {
                Name = DeterministicGuid(from.Key + "|" + to.Key), From = from.Key, To = to.Key,
                AutoGenerated = true, Transport = interSite ? "IP" : null,
            });
        }
        DomainController? Bridgehead(string site) => _dcs.FirstOrDefault(d => d.Site == site && !d.IsReadOnly);

        foreach (var site in _sites)
        {
            var w = _dcs.Where(d => d.Site == site.Name && !d.IsReadOnly).ToList();
            for (var i = 0; i < w.Count && w.Count > 1; i++)
            {
                Add(w[(i + 1) % w.Count], w[i], false);
                Add(w[(i + w.Count - 1) % w.Count], w[i], false);
            }
            foreach (var ro in _dcs.Where(d => d.Site == site.Name && d.IsReadOnly))
            {
                var source = w.FirstOrDefault()
                    ?? _links.Where(l => l.Sites.Contains(site.Name)).SelectMany(l => l.Sites).Where(s => s != site.Name).Select(Bridgehead).FirstOrDefault(b => b != null);
                if (source != null) Add(source, ro, source.Site != site.Name);
            }
        }
        foreach (var link in _links)
            foreach (var a in link.Sites)
                foreach (var b in link.Sites)
                    if (a != b && Bridgehead(a) is { } ba && Bridgehead(b) is { } bb) Add(bb, ba, true);
        result.AddRange(_manual);
        return result;
    }

    string Key(string dcName) => _dcs.FirstOrDefault(d => d.Name == dcName)?.Key ?? throw new ArgumentException($"Unknown DC {dcName}");

    static string OsVersion(string os) => os switch
    {
        _ when os.Contains("2016") => "10.0 (14393)",
        _ when os.Contains("2019") => "10.0 (17763)",
        _ when os.Contains("2022") => "10.0 (20348)",
        _ when os.Contains("2025") => "10.0 (26100)",
        _ => "",
    };

    static string DeterministicGuid(string s) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToString();
}
