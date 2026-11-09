using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adtd.Model;

/// <summary>Reads and writes forest snapshots as indented JSON.</summary>
public static class SnapshotJson
{
    public const int CurrentSchema = 1;

    public static string Serialize(ForestSnapshot snapshot) => JsonSerializer.Serialize(snapshot, SnapshotJsonContext.Default.ForestSnapshot);

    public static ForestSnapshot Deserialize(string json)
    {
        var s = JsonSerializer.Deserialize(json, SnapshotJsonContext.Default.ForestSnapshot)
                ?? throw new InvalidDataException("The file is empty or isn't a forest snapshot.");
        if (s.Schema > CurrentSchema)
            throw new InvalidDataException($"This snapshot was written by a newer version (format {s.Schema}); this version reads format {CurrentSchema}.");
        return s;
    }

    public static void Save(ForestSnapshot snapshot, string path) => File.WriteAllText(path, Serialize(snapshot));
    public static ForestSnapshot Load(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>
    /// The snapshot with every list in a canonical order: domains root first then by tree, everything else by name.
    /// The collector always returns normalised snapshots, so two collections of an unchanged forest compare equal.
    /// </summary>
    public static ForestSnapshot Normalize(ForestSnapshot s)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        return s with
        {
            Domains = s.Domains.OrderBy(d => DomainSortKey(d, s.ForestName), cmp)
                .Select(d => d with { Trusts = d.Trusts.OrderBy(t => t.Partner, cmp).ToList() }).ToList(),
            DomainControllers = s.DomainControllers.OrderBy(d => d.Name, cmp).ThenBy(d => d.Key, cmp)
                .Select(d => d with { IPv4 = d.IPv4.ToList() }).ToList(),
            Sites = s.Sites.OrderBy(x => x.Name, cmp).ToList(),
            Subnets = s.Subnets.OrderBy(x => x.Prefix, SubnetComparer.Instance).ToList(),
            SiteLinks = s.SiteLinks.OrderBy(x => x.Transport, cmp).ThenBy(x => x.Name, cmp)
                .Select(l => l with { Sites = l.Sites.Order(cmp).ToList() }).ToList(),
            SiteLinkBridges = s.SiteLinkBridges.OrderBy(x => x.Name, cmp)
                .Select(b => b with { SiteLinks = b.SiteLinks.Order(cmp).ToList() }).ToList(),
            Connections = s.Connections.OrderBy(c => c.To, cmp).ThenBy(c => c.From, cmp).ThenBy(c => c.Name, cmp).ToList(),
        };
    }

    /// <summary>Forest root first, then each tree root followed by its descendants (reversed DNS labels sort parents before children).</summary>
    static string DomainSortKey(Domain d, string forest)
    {
        var rev = string.Join('.', d.DnsName.Split('.').Reverse());
        return (string.Equals(d.DnsName, forest, StringComparison.OrdinalIgnoreCase) ? "0|" : "1|") + rev;
    }

    /// <summary>Orders "10.2.0.0/16" numerically by address, then by prefix length; anything unparsable sorts last by text.</summary>
    sealed class SubnetComparer : IComparer<string>
    {
        public static readonly SubnetComparer Instance = new();
        public int Compare(string? x, string? y)
        {
            var (ax, lx) = Parse(x); var (ay, ly) = Parse(y);
            var c = ax.CompareTo(ay);
            if (c != 0) return c;
            c = lx.CompareTo(ly);
            return c != 0 ? c : string.CompareOrdinal(x, y);
        }

        static (UInt128 Address, int Length) Parse(string? s)
        {
            if (s == null) return (UInt128.MaxValue, 0);
            var slash = s.IndexOf('/');
            var addr = slash < 0 ? s : s[..slash];
            _ = int.TryParse(slash < 0 ? "" : s[(slash + 1)..], out var len);
            if (!System.Net.IPAddress.TryParse(addr, out var ip)) return (UInt128.MaxValue, 0);
            var bytes = ip.MapToIPv6().GetAddressBytes();
            UInt128 v = 0;
            foreach (var b in bytes) v = (v << 8) | b;
            return (v, len);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ForestSnapshot))]
internal partial class SnapshotJsonContext : JsonSerializerContext;
