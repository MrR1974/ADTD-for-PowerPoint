using Adtd.Model;
using Adtd.Model.Samples;

namespace Adtd.Collect.Tests;

/// <summary>An in-memory directory: a flat list of objects searched with a small LDAP filter evaluator.</summary>
internal sealed class FakeDirectory(IReadOnlyList<LdapEntry> entries, string server) : IDirectory
{
    public string Server { get; } = server;
    public List<string> Log { get; } = [];

    public LdapEntry? Read(string dn, params string[] attributes) =>
        entries.FirstOrDefault(e => string.Equals(e.Dn, dn, StringComparison.OrdinalIgnoreCase)) is { } e ? Project(e, attributes) : null;

    public IReadOnlyList<LdapEntry> Search(string baseDn, string filter, Scope scope, params string[] attributes)
    {
        Log.Add($"{baseDn}|{filter}|{scope}");
        var f = Filter.Parse(filter);
        return entries.Where(e => InScope(e.Dn, baseDn, scope) && f(e)).Select(e => Project(e, attributes)).ToList();
    }

    static bool InScope(string dn, string baseDn, Scope scope)
    {
        if (scope == Scope.Base) return string.Equals(dn, baseDn, StringComparison.OrdinalIgnoreCase);
        if (!dn.EndsWith("," + baseDn, StringComparison.OrdinalIgnoreCase)) return false;
        return scope == Scope.Subtree || string.Equals(Dn.Parent(dn), baseDn, StringComparison.OrdinalIgnoreCase);
    }

    static LdapEntry Project(LdapEntry e, string[] attributes)
    {
        bool Want(string a) => attributes.Length == 0 || attributes.Contains(a, StringComparer.OrdinalIgnoreCase);
        return new LdapEntry(e.Dn,
            e.Values.Where(kv => Want(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            e.Binary.Where(kv => Want(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));
    }

    public void Dispose() { }
}

/// <summary>Parses (&amp;..), (|..), (!..), (attr=value) and (attr=*) filters.</summary>
internal static class Filter
{
    public static Func<LdapEntry, bool> Parse(string s)
    {
        var i = 0;
        var f = One(s, ref i);
        if (i != s.Length) throw new FormatException($"Trailing text in filter {s}");
        return f;
    }

    static Func<LdapEntry, bool> One(string s, ref int i)
    {
        if (s[i] != '(') throw new FormatException($"Expected ( at {i} in {s}");
        i++;
        Func<LdapEntry, bool> result;
        switch (s[i])
        {
            case '&' or '|':
                var op = s[i++];
                var parts = new List<Func<LdapEntry, bool>>();
                while (s[i] == '(') parts.Add(One(s, ref i));
                result = op == '&' ? e => parts.All(p => p(e)) : e => parts.Any(p => p(e));
                break;
            case '!':
                i++;
                var inner = One(s, ref i);
                result = e => !inner(e);
                break;
            default:
                var end = s.IndexOf(')', i);
                var item = s[i..end];
                var eq = item.IndexOf('=');
                var (attr, value) = (item[..eq], item[(eq + 1)..]);
                result = value == "*"
                    ? e => e.GetAll(attr).Length > 0 || e.GetBinary(attr) != null
                    : e => e.GetAll(attr).Contains(value, StringComparer.OrdinalIgnoreCase);
                i = end;
                break;
        }
        if (s[i] != ')') throw new FormatException($"Expected ) at {i} in {s}");
        i++;
        return result;
    }
}

/// <summary>Serves one DIT for every connection; connections to the listed targets fail.</summary>
internal sealed class FakeFactory(IReadOnlyList<LdapEntry> dit, params string[] unreachable) : IDirectoryFactory
{
    public List<string> Connected { get; } = [];

    public IDirectory Connect(string serverOrDomain, bool globalCatalog = false)
    {
        if (unreachable.Contains(serverOrDomain, StringComparer.OrdinalIgnoreCase))
            throw new DirectoryUnavailableException(serverOrDomain, "The LDAP server is unavailable.");
        Connected.Add(globalCatalog ? serverOrDomain + ":3268" : serverOrDomain);
        return new FakeDirectory(dit, serverOrDomain);
    }
}

internal sealed class FakeResolver(IReadOnlyDictionary<string, string[]> map) : IIpResolver
{
    public Task<IReadOnlyDictionary<string, string[]>> ResolveAsync(IReadOnlyCollection<string> hosts, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, string[]>>(
            hosts.Where(map.ContainsKey).Where(h => map[h].Length > 0).ToDictionary(h => h, h => map[h], StringComparer.OrdinalIgnoreCase));

    public static FakeResolver For(ForestSnapshot s) =>
        new(s.DomainControllers.Where(d => d.DnsHostName != null).ToDictionary(d => d.DnsHostName!, d => d.IPv4.ToArray(), StringComparer.OrdinalIgnoreCase));
}

/// <summary>Lays a snapshot out as the directory objects AD would hold for it.</summary>
internal static class Dit
{
    public static List<LdapEntry> From(ForestSnapshot s)
    {
        var list = new List<LdapEntry>();
        var rootNc = ForestBuilder.DnOf(s.ForestName);
        var config = "CN=Configuration," + rootNc;
        var schema = "CN=Schema," + config;
        var sites = "CN=Sites," + config;
        var transports = "CN=Inter-Site Transports," + sites;
        void Add(string dn, params (string Attr, object? Value)[] attrs)
        {
            var values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var binary = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (a, v) in attrs)
                switch (v)
                {
                    case null: break;
                    case byte[] b: binary[a] = b; break;
                    case string[] arr: values[a] = arr; break;
                    case bool bo: values[a] = [bo ? "TRUE" : "FALSE"]; break;
                    default: values[a] = [Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!]; break;
                }
            list.Add(new LdapEntry(dn, values, binary));
        }
        string SiteDn(string name) => $"CN={name},{sites}";

        Add("", ("configurationNamingContext", config), ("schemaNamingContext", schema), ("rootDomainNamingContext", rootNc),
            ("dnsHostName", s.DomainControllers.FirstOrDefault()?.DnsHostName));
        Add($"CN=Partitions,{config}", ("objectClass", new[] { "top", "crossRefContainer" }), ("fSMORoleOwner", s.DomainNamingMaster), ("msDS-Behavior-Version", s.ForestFunctionalLevel));
        Add(schema, ("objectClass", new[] { "top", "dMD" }), ("fSMORoleOwner", s.SchemaMaster), ("objectVersion", s.SchemaVersion));
        // an application partition crossRef, which has no netBIOSName and must be ignored
        Add($"CN=DomainDnsZones,CN=Partitions,{config}", ("objectClass", new[] { "top", "crossRef" }), ("nCName", "DC=DomainDnsZones," + rootNc), ("dnsRoot", "DomainDnsZones." + s.ForestName));

        foreach (var d in s.Domains)
        {
            Add($"CN={d.NetBiosName},CN=Partitions,{config}", ("objectClass", new[] { "top", "crossRef" }), ("nCName", d.DistinguishedName),
                ("dnsRoot", d.DnsName), ("netBIOSName", d.NetBiosName), ("msDS-Behavior-Version", d.FunctionalLevel));
            Add(d.DistinguishedName, ("objectClass", new[] { "top", "domain", "domainDNS" }), ("fSMORoleOwner", d.PdcMaster));
            Add($"CN=RID Manager$,CN=System,{d.DistinguishedName}", ("objectClass", new[] { "top", "rIDManager" }), ("fSMORoleOwner", d.RidMaster));
            Add($"CN=Infrastructure,{d.DistinguishedName}", ("objectClass", new[] { "top", "infrastructureUpdate" }), ("fSMORoleOwner", d.InfrastructureMaster));
            foreach (var t in d.Trusts)
                Add($"CN={t.Partner},CN=System,{d.DistinguishedName}", ("objectClass", new[] { "top", "leaf", "trustedDomain" }),
                    ("trustPartner", t.Partner), ("flatName", t.FlatName), ("trustDirection", (int)t.Direction), ("trustType", t.TrustType), ("trustAttributes", (int)t.Attributes));
        }

        foreach (var site in s.Sites)
        {
            Add(SiteDn(site.Name), ("objectClass", new[] { "top", "site" }), ("name", site.Name), ("description", site.Description), ("location", site.Location));
            Add($"CN=NTDS Site Settings,{SiteDn(site.Name)}", ("objectClass", new[] { "top", "applicationSiteSettings", "nTDSSiteSettings" }),
                ("options", site.Options == 0 ? null : site.Options), ("interSiteTopologyGenerator", site.Istg));
            Add($"CN=Servers,{SiteDn(site.Name)}", ("objectClass", new[] { "top", "serversContainer" }));
        }

        foreach (var dc in s.DomainControllers)
        {
            var domain = s.Domains.First(d => d.DnsName == dc.Domain);
            var computer = $"CN={dc.Name},OU=Domain Controllers,{domain.DistinguishedName}";
            var serverDn = Dn.Parent(dc.Key);
            Add(serverDn, ("objectClass", new[] { "top", "server" }), ("name", dc.Name), ("dNSHostName", dc.DnsHostName), ("serverReference", computer));
            Add(dc.Key, ("objectClass", dc.IsReadOnly ? new[] { "top", "applicationSettings", "nTDSDSA", "nTDSDSARO" } : new[] { "top", "applicationSettings", "nTDSDSA" }),
                ("options", dc.IsGlobalCatalog ? 1 : 0), ("msDS-HasDomainNCs", domain.DistinguishedName));
            Add(computer, ("objectClass", new[] { "top", "person", "user", "computer" }), ("dNSHostName", dc.DnsHostName),
                ("operatingSystem", dc.OperatingSystem), ("operatingSystemVersion", dc.OperatingSystemVersion));
        }

        foreach (var c in s.Connections)
            Add($"CN={c.Name},{c.To}", ("objectClass", new[] { "top", "leaf", "connectionPoint", "nTDSConnection" }), ("name", c.Name),
                ("fromServer", c.From), ("options", c.AutoGenerated ? 1 : 0), ("enabledConnection", c.Enabled),
                ("transportType", c.Transport == null ? null : $"CN={c.Transport},{transports}"));

        foreach (var sn in s.Subnets)
            Add($"CN={sn.Prefix},CN=Subnets,{sites}", ("objectClass", new[] { "top", "subnet" }), ("name", sn.Prefix),
                ("siteObject", sn.Site == null ? null : SiteDn(sn.Site)), ("location", sn.Location), ("description", sn.Description));

        foreach (var t in s.SiteLinks.Select(l => l.Transport).Concat(["IP", "SMTP"]).Distinct())
            Add($"CN={t},{transports}", ("objectClass", new[] { "top", "interSiteTransport" }));
        foreach (var l in s.SiteLinks)
            Add($"CN={l.Name},CN={l.Transport},{transports}", ("objectClass", new[] { "top", "siteLink" }), ("name", l.Name),
                ("siteList", l.Sites.Select(SiteDn).ToArray()), ("cost", l.Cost), ("replInterval", l.ReplicationIntervalMinutes),
                ("options", l.Options == 0 ? null : l.Options), ("schedule", l.ScheduleHours is int h ? ScheduleBlob.Encode(h) : null));
        foreach (var b in s.SiteLinkBridges)
            Add($"CN={b.Name},CN={b.Transport},{transports}", ("objectClass", new[] { "top", "siteLinkBridge" }), ("name", b.Name),
                ("siteLinkList", b.SiteLinks.Select(l => $"CN={l},CN={b.Transport},{transports}").ToArray()));
        return list;
    }
}
