using Adtd.Model;
using Adtd.Model.Samples;

namespace Adtd.Model.Tests;

public class SnapshotJsonTests
{
    [Fact]
    public void Round_trips_without_loss()
    {
        var s = SampleForests.Contoso();
        var json = SnapshotJson.Serialize(s);
        Assert.Equal(json, SnapshotJson.Serialize(SnapshotJson.Deserialize(json)));
    }

    [Fact]
    public void Writes_readable_json()
    {
        var json = SnapshotJson.Serialize(SampleForests.Contoso());
        Assert.Contains("\"forestName\": \"contoso.com\"", json);
        Assert.Contains("\"kind\": \"Realm\"", json);                 // enums as names
        Assert.Contains("\"direction\": \"Bidirectional\"", json);
        Assert.DoesNotContain("universalGroupCaching", json);         // derived properties aren't stored
        Assert.DoesNotContain("\"istg\": null", json);                 // nulls are left out
    }

    [Fact]
    public void Refuses_snapshots_from_a_newer_version()
    {
        var json = SnapshotJson.Serialize(SampleForests.Single()).Replace("\"schema\": 1", "\"schema\": 99");
        var ex = Assert.Throws<InvalidDataException>(() => SnapshotJson.Deserialize(json));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void Normalising_is_stable_and_puts_the_forest_root_first()
    {
        var s = SampleForests.Contoso();
        var shuffled = s with { Domains = s.Domains.Reverse().ToList(), Subnets = s.Subnets.Reverse().ToList() };
        var n = SnapshotJson.Normalize(shuffled);
        Assert.Equal(SnapshotJson.Serialize(s), SnapshotJson.Serialize(n));
        Assert.Equal(["contoso.com", "apac.contoso.com", "emea.contoso.com", "fabrikam.com"], n.Domains.Select(d => d.DnsName));
        // numeric, not alphabetical: 10.2 before 10.10
        var prefixes = n.Subnets.Select(x => x.Prefix).ToList();
        Assert.True(prefixes.IndexOf("10.2.0.0/20") < prefixes.IndexOf("10.10.0.0/20"));
    }
}

public class SampleForestTests
{
    [Theory]
    [InlineData("single", 1, 2, 3)]
    [InlineData("contoso", 4, 18, 30)]
    [InlineData("large", 3, 150, 400)]
    public void Have_the_advertised_size(string name, int domains, int sites, int dcs)
    {
        var s = SampleForests.ByName(name);
        Assert.Equal(domains, s.Domains.Count);
        Assert.Equal(sites, s.Sites.Count);
        Assert.Equal(dcs, s.DomainControllers.Count);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("contoso")]
    [InlineData("large")]
    public void Are_internally_consistent(string name)
    {
        var s = SampleForests.ByName(name);
        var keys = s.DomainControllers.Select(d => d.Key).ToHashSet();
        var sites = s.Sites.Select(x => x.Name).ToHashSet();
        Assert.Equal(s.DomainControllers.Count, keys.Count);
        Assert.All(s.Connections, c => { Assert.Contains(c.From, keys); Assert.Contains(c.To, keys); });
        Assert.All(s.SiteLinks.SelectMany(l => l.Sites), x => Assert.Contains(x, sites));
        Assert.All(s.DomainControllers, d => Assert.Contains(d.Site, sites));
        Assert.All(s.Domains, d => Assert.Contains(d.PdcMaster!, keys));
        Assert.All(s.Sites.Where(x => x.Istg != null), x => Assert.Contains(x.Istg!, keys));
        Assert.Equal(s.Subnets.Count, s.Subnets.Select(x => x.Prefix).Distinct().Count());
    }

    [Fact]
    public void Are_deterministic() =>
        Assert.Equal(SnapshotJson.Serialize(SampleForests.Large()), SnapshotJson.Serialize(SampleForests.Large()));
}

public class TrustTests
{
    static readonly Dictionary<string, string?> Forest = Trusts.Parents(["contoso.com", "emea.contoso.com", "uk.emea.contoso.com", "fabrikam.com"]);

    [Fact]
    public void Finds_parents_by_longest_DNS_suffix()
    {
        Assert.Null(Forest["contoso.com"]);
        Assert.Equal("contoso.com", Forest["emea.contoso.com"]);
        Assert.Equal("emea.contoso.com", Forest["uk.emea.contoso.com"]);
        Assert.Null(Forest["fabrikam.com"]);
    }

    [Theory]
    [InlineData("uk.emea.contoso.com", "emea.contoso.com", 2, TrustAttributes.WithinForest, TrustKind.ParentChild)]
    [InlineData("emea.contoso.com", "uk.emea.contoso.com", 2, TrustAttributes.WithinForest, TrustKind.ParentChild)]
    [InlineData("fabrikam.com", "contoso.com", 2, TrustAttributes.WithinForest, TrustKind.TreeRoot)]
    [InlineData("uk.emea.contoso.com", "contoso.com", 2, TrustAttributes.WithinForest, TrustKind.Shortcut)]
    [InlineData("contoso.com", "other.example", 2, TrustAttributes.ForestTransitive, TrustKind.Forest)]
    [InlineData("contoso.com", "nt4.example", 1, TrustAttributes.None, TrustKind.External)]
    [InlineData("contoso.com", "REALM.EXAMPLE", 3, TrustAttributes.NonTransitive, TrustKind.Realm)]
    public void Classifies(string domain, string partner, int type, TrustAttributes attrs, TrustKind expected) =>
        Assert.Equal(expected, Trusts.Classify(domain, partner, type, attrs, Forest));
}

public class NameTests
{
    [Theory]
    [InlineData("Windows Server 2022 Datacenter", "WS 2022 DC")]
    [InlineData("Windows Server® 2008 Enterprise Service Pack 2", "WS 2008 Ent SP2")]
    [InlineData("Windows Server 2025 Datacenter Evaluation", "WS 2025 DC Eval")]
    [InlineData(null, "")]
    public void Shortens_operating_systems(string? os, string expected) => Assert.Equal(expected, OsNames.Short(os));

    [Fact]
    public void Names_versions()
    {
        Assert.Equal("Windows Server 2016", FunctionalLevels.Name(7));
        Assert.Equal("Windows Server 2025", FunctionalLevels.Name(10));
        Assert.Equal("Windows Server 2019/2022", FunctionalLevels.SchemaName(88));
        Assert.Equal("Unknown (99)", FunctionalLevels.SchemaName(99));
    }
}
