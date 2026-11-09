using System.Diagnostics;
using System.Reflection;
using Adtd.Model;

namespace Adtd.Collect;

public sealed record CollectOptions
{
    /// <summary>Look up each DC's IPv4 addresses in DNS.</summary>
    public bool ResolveAddresses { get; init; } = true;
}

public enum CollectLevel { Step, Detail, Warning }

public sealed record CollectEvent(CollectLevel Level, string Message);

/// <summary>
/// Reads a forest's topology over LDAP. The queries follow ADTD.Net's LdapReader, DomainDataReader and SiteDataReader
/// (MIT, Goran Mihelcic), extended with subnets, site link bridges and DC addresses.
///
/// Everything forest-wide comes from the configuration and schema partitions of the first DC reached. Each domain's
/// FSMO roles, trusts and DC computer objects come from a DC of that domain; if one can't be reached the domain is
/// marked unreachable, its DCs' operating systems are read from a global catalog instead, and collection carries on.
/// </summary>
public sealed class ForestReader(IDirectoryFactory factory, IIpResolver? resolver = null, IProgress<CollectEvent>? progress = null)
{
    readonly List<string> _warnings = [];

    public static string Version =>
        typeof(ForestReader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public async Task<ForestSnapshot> ReadAsync(string target, CollectOptions? options = null, CancellationToken ct = default)
    {
        options ??= new CollectOptions();
        _warnings.Clear();
        var started = Stopwatch.StartNew();

        Step($"Connecting to {target}");
        using var root = Logged(factory.Connect(target));
        var dse = root.Read("", "configurationNamingContext", "schemaNamingContext", "rootDomainNamingContext", "dnsHostName")
                  ?? throw new DirectoryUnavailableException(target, "RootDSE couldn't be read.");
        string config = Need(dse, "configurationNamingContext"), schema = Need(dse, "schemaNamingContext"), rootNc = Need(dse, "rootDomainNamingContext");
        var host = dse.Get("dnsHostName") ?? target;
        var forestName = Dn.ToDns(rootNc);
        Step($"Bound to {host}; forest {forestName}");

        // ---- forest ----
        var partitions = root.Read($"CN=Partitions,{config}", "fSMORoleOwner", "msDS-Behavior-Version");
        var schemaHead = root.Read(schema, "fSMORoleOwner", "objectVersion");
        var crossRefs = root.Search($"CN=Partitions,{config}", "(&(objectClass=crossRef)(netBIOSName=*))", Scope.OneLevel,
            "nCName", "dnsRoot", "netBIOSName", "msDS-Behavior-Version");
        ct.ThrowIfCancellationRequested();

        var domains = crossRefs
            .Where(c => c.Get("nCName") != null && c.Get("dnsRoot") != null)
            .Select(c => new Domain
            {
                DnsName = c.Get("dnsRoot")!,
                NetBiosName = c.Get("netBIOSName") ?? "",
                DistinguishedName = c.Get("nCName")!,
                FunctionalLevel = c.GetInt("msDS-Behavior-Version"),
            }).ToList();
        var parents = Trusts.Parents(domains.Select(d => d.DnsName));
        domains = domains.Select(d => d with { ParentDns = parents[d.DnsName] }).ToList();
        var domainByNc = domains.ToDictionary(d => d.DistinguishedName, d => d.DnsName, StringComparer.OrdinalIgnoreCase);

        // ---- sites, servers, DCs, connections ----
        var sitesBase = $"CN=Sites,{config}";
        Step("Reading sites and domain controllers");
        var siteEntries = root.Search(sitesBase, "(objectClass=site)", Scope.OneLevel, "name", "description", "location");
        var settings = root.Search(sitesBase, "(objectClass=nTDSSiteSettings)", Scope.Subtree, "options", "interSiteTopologyGenerator")
            .ToDictionary(e => Dn.Parent(e.Dn), StringComparer.OrdinalIgnoreCase);
        var servers = root.Search(sitesBase, "(objectClass=server)", Scope.Subtree, "name", "dNSHostName", "serverReference")
            .ToDictionary(e => e.Dn, StringComparer.OrdinalIgnoreCase);
        var dsas = root.Search(sitesBase, "(objectClass=nTDSDSA)", Scope.Subtree, "objectClass", "options", "msDS-HasDomainNCs");
        var connEntries = root.Search(sitesBase, "(objectClass=nTDSConnection)", Scope.Subtree, "name", "fromServer", "options", "enabledConnection", "transportType");
        ct.ThrowIfCancellationRequested();

        var sites = siteEntries.Select(e =>
        {
            var s = settings.GetValueOrDefault(e.Dn);
            return new Site
            {
                Name = e.Get("name") ?? Dn.Value(e.Dn),
                Description = e.Get("description"),
                Location = e.Get("location"),
                Options = s?.GetInt("options") ?? 0,
                Istg = s?.Get("interSiteTopologyGenerator") is string istg && !Dn.IsDeleted(istg) ? istg : null,
            };
        }).ToList();

        var dcs = new List<DomainController>();
        var serverRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // DC key → computer DN
        foreach (var dsa in dsas)
        {
            var serverDn = Dn.Parent(dsa.Dn);
            var server = servers.GetValueOrDefault(serverDn);
            var name = server?.Get("name") ?? Dn.Value(serverDn);
            var serverRef = server?.Get("serverReference");
            var domain = dsa.GetAll("msDS-HasDomainNCs").Select(nc => domainByNc.GetValueOrDefault(nc)).FirstOrDefault(d => d != null)
                         ?? (serverRef != null ? domains.FirstOrDefault(d => serverRef.EndsWith(d.DistinguishedName, StringComparison.OrdinalIgnoreCase)
                                                     && !domains.Any(o => o.DistinguishedName.Length > d.DistinguishedName.Length && serverRef.EndsWith(o.DistinguishedName, StringComparison.OrdinalIgnoreCase)))?.DnsName : null);
            if (domain == null) Warn($"Couldn't tell which domain {name} belongs to.");
            if (serverRef != null) serverRefs[dsa.Dn] = serverRef;
            dcs.Add(new DomainController
            {
                Key = dsa.Dn,
                Name = name,
                DnsHostName = server?.Get("dNSHostName"),
                Domain = domain,
                Site = Dn.ValueAt(dsa.Dn, 3),
                IsGlobalCatalog = (dsa.GetInt("options") & 1) != 0,
                IsReadOnly = dsa.GetAll("objectClass").Contains("nTDSDSARO", StringComparer.OrdinalIgnoreCase),
            });
        }
        var dcKeys = dcs.Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var connections = new List<ReplicationConnection>();
        foreach (var c in connEntries)
        {
            var to = Dn.Parent(c.Dn);
            var from = c.Get("fromServer");
            if (from == null || !dcKeys.Contains(to)) continue;
            if (!dcKeys.Contains(from))
            {
                Warn($"Connection {c.Get("name")} to {Dn.ValueAt(to, 1)} replicates from {(Dn.IsDeleted(from) ? "a deleted DC" : "an unknown DC")}; left out.");
                continue;
            }
            connections.Add(new ReplicationConnection
            {
                Name = c.Get("name") ?? Dn.Value(c.Dn),
                From = from,
                To = to,
                Enabled = c.GetBool("enabledConnection", true),
                AutoGenerated = (c.GetInt("options") & 1) != 0,
                Transport = c.Get("transportType") is string t ? Dn.Value(t) : null,
            });
        }

        // ---- subnets, site links, bridges ----
        Step("Reading subnets and site links");
        var subnets = root.Search($"CN=Subnets,{sitesBase}", "(objectClass=subnet)", Scope.OneLevel, "name", "siteObject", "location", "description")
            .Select(e => new Subnet
            {
                Prefix = e.Get("name") ?? Dn.Value(e.Dn),
                Site = e.Get("siteObject") is string so ? Dn.Value(so) : null,
                Location = e.Get("location"),
                Description = e.Get("description"),
            }).ToList();
        var transports = $"CN=Inter-Site Transports,{sitesBase}";
        var links = root.Search(transports, "(objectClass=siteLink)", Scope.Subtree, "name", "siteList", "cost", "replInterval", "options", "schedule")
            .Select(e => new SiteLink
            {
                Name = e.Get("name") ?? Dn.Value(e.Dn),
                Transport = Dn.ValueAt(e.Dn, 1),
                Sites = e.GetAll("siteList").Select(Dn.Value).ToList(),
                Cost = e.GetInt("cost"),
                ReplicationIntervalMinutes = e.GetInt("replInterval"),
                Options = e.GetInt("options"),
                ScheduleHours = ScheduleBlob.AvailableHours(e.GetBinary("schedule")),
            }).ToList();
        var bridges = root.Search(transports, "(objectClass=siteLinkBridge)", Scope.Subtree, "name", "siteLinkList")
            .Select(e => new SiteLinkBridge
            {
                Name = e.Get("name") ?? Dn.Value(e.Dn),
                Transport = Dn.ValueAt(e.Dn, 1),
                SiteLinks = e.GetAll("siteLinkList").Select(Dn.Value).ToList(),
            }).ToList();
        Step($"Found {domains.Count} domain(s), {sites.Count} site(s), {dcs.Count} domain controller(s), {subnets.Count} subnet(s), {links.Count} site link(s)");

        // ---- per domain: FSMO roles, trusts, DC computer objects ----
        IDirectory? gc = null;
        try
        {
            for (var i = 0; i < domains.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var d = domains[i];
                Step($"Reading domain {d.DnsName}");
                var computers = dcs.Where(dc => string.Equals(dc.Domain, d.DnsName, StringComparison.OrdinalIgnoreCase)).ToList();
                IDirectory? dir = null;
                try { dir = Logged(factory.Connect(d.DnsName)); }
                catch (DirectoryUnavailableException ex)
                {
                    Warn($"Domain {d.DnsName} couldn't be reached ({ex.Message}); its FSMO roles and trusts are missing.");
                    domains[i] = d with { Reachable = false };
                    if (computers.Count > 0)
                    {
                        try { gc ??= Logged(factory.Connect(target, globalCatalog: true)); }
                        catch (DirectoryUnavailableException gex) { Warn($"Global catalog couldn't be reached ({gex.Message})."); }
                        if (gc != null) ReadComputers(gc, computers, serverRefs, dcs);
                    }
                    continue;
                }
                using (dir)
                {
                    string? Owner(string dn)
                    {
                        var owner = dir.Read(dn, "fSMORoleOwner")?.Get("fSMORoleOwner");
                        if (owner != null && Dn.IsDeleted(owner)) Warn($"A FSMO role in {d.DnsName} is held by a deleted DC ({Dn.ValueAt(owner, 1)}).");
                        return owner;
                    }
                    var trusts = dir.Search($"CN=System,{d.DistinguishedName}", "(objectClass=trustedDomain)", Scope.OneLevel,
                            "trustPartner", "flatName", "trustDirection", "trustType", "trustAttributes")
                        .Where(t => t.Get("trustPartner") != null)
                        .Select(t =>
                        {
                            var partner = t.Get("trustPartner")!;
                            var type = t.GetInt("trustType");
                            var attrs = (TrustAttributes)t.GetInt("trustAttributes");
                            return new Trust
                            {
                                Partner = partner,
                                FlatName = t.Get("flatName"),
                                Direction = (TrustDirection)t.GetInt("trustDirection"),
                                TrustType = type,
                                Attributes = attrs,
                                Kind = Trusts.Classify(d.DnsName, partner, type, attrs, parents),
                            };
                        }).ToList();
                    domains[i] = d with
                    {
                        PdcMaster = Owner(d.DistinguishedName),
                        RidMaster = Owner($"CN=RID Manager$,CN=System,{d.DistinguishedName}"),
                        InfrastructureMaster = Owner($"CN=Infrastructure,{d.DistinguishedName}"),
                        Trusts = trusts,
                    };
                    ReadComputers(dir, computers, serverRefs, dcs);
                }
            }
        }
        finally { gc?.Dispose(); }

        // ---- DC addresses ----
        if (options.ResolveAddresses && resolver != null)
        {
            var hosts = dcs.Select(d => d.DnsHostName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Step($"Resolving addresses of {hosts.Count} domain controller(s)");
            var found = await resolver.ResolveAsync(hosts, ct);
            for (var i = 0; i < dcs.Count; i++)
            {
                var ips = dcs[i].DnsHostName is string h && found.TryGetValue(h, out var a) ? a : [];
                if (ips.Length == 0) Warn($"No IPv4 address found for {dcs[i].DnsHostName ?? dcs[i].Name}.");
                dcs[i] = dcs[i] with { IPv4 = ips };
            }
        }

        // ---- consistency warnings ----
        var siteNames = sites.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in subnets.Where(s => s.Site == null)) Warn($"Subnet {s.Prefix} isn't associated with a site.");
        foreach (var l in links.Where(l => l.Sites.Count < 2)) Warn($"Site link {l.Name} has fewer than two sites.");
        foreach (var d in domains.Where(d => d.Reachable))
            foreach (var (role, key) in new[] { ("PDC", d.PdcMaster), ("RID master", d.RidMaster), ("Infrastructure master", d.InfrastructureMaster) })
                if (key != null && !Dn.IsDeleted(key) && !dcKeys.Contains(key)) Warn($"The {role} of {d.DnsName} isn't a known DC ({key}).");

        Step($"Done in {started.Elapsed.TotalSeconds:0.0} s with {_warnings.Count} warning(s)");
        return SnapshotJson.Normalize(new ForestSnapshot
        {
            ForestName = forestName,
            CollectedAt = DateTimeOffset.Now,
            CollectedFrom = host,
            CollectorVersion = Version,
            ForestFunctionalLevel = partitions?.GetInt("msDS-Behavior-Version") ?? 0,
            SchemaVersion = schemaHead?.GetInt("objectVersion") ?? 0,
            SchemaMaster = schemaHead?.Get("fSMORoleOwner"),
            DomainNamingMaster = partitions?.Get("fSMORoleOwner"),
            Domains = domains,
            DomainControllers = dcs,
            Sites = sites,
            Subnets = subnets,
            SiteLinks = links,
            SiteLinkBridges = bridges,
            Connections = connections,
            Warnings = _warnings.ToList(),
        });
    }

    /// <summary>Fills in the operating system (and DNS name, if the server object had none) from each DC's computer object.</summary>
    void ReadComputers(IDirectory dir, List<DomainController> computers, Dictionary<string, string> serverRefs, List<DomainController> all)
    {
        foreach (var dc in computers)
        {
            if (!serverRefs.TryGetValue(dc.Key, out var computerDn))
            {
                Warn($"{dc.Name} has no computer object reference (serverReference).");
                continue;
            }
            var c = dir.Read(computerDn, "operatingSystem", "operatingSystemVersion", "dNSHostName");
            if (c == null) { Warn($"Computer object of {dc.Name} couldn't be read."); continue; }
            var i = all.FindIndex(x => x.Key == dc.Key);
            all[i] = all[i] with
            {
                OperatingSystem = c.Get("operatingSystem"),
                OperatingSystemVersion = c.Get("operatingSystemVersion"),
                DnsHostName = all[i].DnsHostName ?? c.Get("dNSHostName"),
            };
        }
    }

    IDirectory Logged(IDirectory d) => new LoggingDirectory(d, m => progress?.Report(new CollectEvent(CollectLevel.Detail, m)));
    void Step(string m) => progress?.Report(new CollectEvent(CollectLevel.Step, m));
    void Warn(string m) { _warnings.Add(m); progress?.Report(new CollectEvent(CollectLevel.Warning, m)); }
    static string Need(LdapEntry e, string attr) => e.Get(attr) ?? throw new InvalidDataException($"RootDSE has no {attr}.");
}

/// <summary>Reports every LDAP operation, its result count and time: what a remote collection log needs.</summary>
internal sealed class LoggingDirectory(IDirectory inner, Action<string> log) : IDirectory
{
    public string Server => inner.Server;

    public LdapEntry? Read(string dn, params string[] attributes)
    {
        var sw = Stopwatch.StartNew();
        var r = inner.Read(dn, attributes);
        log($"{inner.Server}: read \"{dn}\" → {(r == null ? "not found" : "ok")} ({sw.ElapsedMilliseconds} ms)");
        return r;
    }

    public IReadOnlyList<LdapEntry> Search(string baseDn, string filter, Scope scope, params string[] attributes)
    {
        var sw = Stopwatch.StartNew();
        var r = inner.Search(baseDn, filter, scope, attributes);
        log($"{inner.Server}: search \"{baseDn}\" {filter} [{scope}] → {r.Count} ({sw.ElapsedMilliseconds} ms)");
        return r;
    }

    public void Dispose() => inner.Dispose();
}
