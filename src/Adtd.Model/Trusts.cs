namespace Adtd.Model;

public static class Trusts
{
    /// <summary>
    /// What kind of trust a trustedDomain object is, from the trusting domain's point of view.
    /// ADTD.Net cast the raw trustType to System.DirectoryServices' TrustType enum, which has different meanings;
    /// this classifies from trustType, trustAttributes and the forest's own domain tree instead.
    /// </summary>
    /// <param name="domain">DNS name of the domain holding the trust.</param>
    /// <param name="partner">trustPartner.</param>
    /// <param name="forestDomains">DNS name → parent DNS name (null for tree roots) for every domain in the forest.</param>
    public static TrustKind Classify(string domain, string partner, int trustType, TrustAttributes attributes,
                                     IReadOnlyDictionary<string, string?> forestDomains)
    {
        if (trustType == 3) return TrustKind.Realm;
        if (attributes.HasFlag(TrustAttributes.ForestTransitive)) return TrustKind.Forest;
        var inForest = attributes.HasFlag(TrustAttributes.WithinForest) || forestDomains.ContainsKey(partner);
        if (!inForest) return trustType is 1 or 2 ? TrustKind.External : TrustKind.Unknown;

        var parentOfDomain = forestDomains.GetValueOrDefault(domain);
        var parentOfPartner = forestDomains.GetValueOrDefault(partner);
        if (Same(parentOfDomain, partner) || Same(parentOfPartner, domain)) return TrustKind.ParentChild;
        if (forestDomains.ContainsKey(domain) && forestDomains.ContainsKey(partner) && parentOfDomain == null && parentOfPartner == null)
            return TrustKind.TreeRoot;
        return TrustKind.Shortcut;
    }

    /// <summary>The parent of each domain: the forest domain whose DNS name is the longest proper suffix of it.</summary>
    public static Dictionary<string, string?> Parents(IEnumerable<string> domainDnsNames)
    {
        var names = domainDnsNames.ToList();
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in names)
            result[d] = names.Where(p => d.EndsWith("." + p, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(p => p.Length).FirstOrDefault();
        return result;
    }

    static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
