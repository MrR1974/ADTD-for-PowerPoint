using System.Text.Json.Serialization;

namespace Adtd.Model;

/// <summary>
/// Everything collected from one Active Directory forest. It's the hand-off between the collector and the
/// diagrams, and it's saved as JSON so a forest collected on one machine can be drawn on another.
/// Domain controllers are referred to by <see cref="DomainController.Key"/>, the DN of their NTDS Settings object,
/// because that's how AD itself refers to them (fSMORoleOwner, fromServer, interSiteTopologyGenerator).
/// </summary>
public sealed record ForestSnapshot
{
    /// <summary>Snapshot format version; bump when a change would break older readers.</summary>
    public int Schema { get; init; } = SnapshotJson.CurrentSchema;

    public required string ForestName { get; init; }
    public DateTimeOffset CollectedAt { get; init; }
    /// <summary>The DC the collector bound to (or "sample" for generated forests).</summary>
    public string CollectedFrom { get; init; } = "";
    public string CollectorVersion { get; init; } = "";

    /// <summary>msDS-Behavior-Version of CN=Partitions. See <see cref="FunctionalLevels"/>.</summary>
    public int ForestFunctionalLevel { get; init; }
    /// <summary>objectVersion of the schema NC. See <see cref="FunctionalLevels.SchemaName"/>.</summary>
    public int SchemaVersion { get; init; }
    public string? SchemaMaster { get; init; }
    public string? DomainNamingMaster { get; init; }

    public IReadOnlyList<Domain> Domains { get; init; } = [];
    public IReadOnlyList<DomainController> DomainControllers { get; init; } = [];
    public IReadOnlyList<Site> Sites { get; init; } = [];
    public IReadOnlyList<Subnet> Subnets { get; init; } = [];
    public IReadOnlyList<SiteLink> SiteLinks { get; init; } = [];
    public IReadOnlyList<SiteLinkBridge> SiteLinkBridges { get; init; } = [];
    public IReadOnlyList<ReplicationConnection> Connections { get; init; } = [];

    /// <summary>Anything the collector couldn't read or had to guess, in plain words.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record Domain
{
    public required string DnsName { get; init; }
    public string NetBiosName { get; init; } = "";
    public required string DistinguishedName { get; init; }
    /// <summary>DNS name of the parent domain; null for a tree root.</summary>
    public string? ParentDns { get; init; }
    /// <summary>msDS-Behavior-Version of the domain's crossRef.</summary>
    public int FunctionalLevel { get; init; }
    public string? PdcMaster { get; init; }
    public string? RidMaster { get; init; }
    public string? InfrastructureMaster { get; init; }
    public IReadOnlyList<Trust> Trusts { get; init; } = [];
    /// <summary>False when the domain's own DCs couldn't be reached; FSMO roles, trusts and DC OS are then missing.</summary>
    public bool Reachable { get; init; } = true;
}

public enum TrustDirection { Disabled = 0, Inbound = 1, Outbound = 2, Bidirectional = 3 }

public enum TrustKind { ParentChild, TreeRoot, Shortcut, External, Forest, Realm, Unknown }

/// <summary>trustAttributes bits (MS-ADTS 6.1.6.7.9).</summary>
[Flags]
public enum TrustAttributes
{
    None = 0,
    NonTransitive = 0x1,
    UplevelOnly = 0x2,
    QuarantinedDomain = 0x4,
    ForestTransitive = 0x8,
    CrossOrganization = 0x10,
    WithinForest = 0x20,
    TreatAsExternal = 0x40,
    UsesRc4Encryption = 0x80,
    CrossOrganizationNoTgtDelegation = 0x200,
    PimTrust = 0x400,
}

/// <summary>A trustedDomain object in one domain's CN=System. In-forest trusts appear once from each side.</summary>
public sealed record Trust
{
    public required string Partner { get; init; }
    public string? FlatName { get; init; }
    public TrustDirection Direction { get; init; }
    public TrustKind Kind { get; init; }
    /// <summary>trustType: 1 downlevel (NT4), 2 uplevel (AD), 3 MIT Kerberos realm.</summary>
    public int TrustType { get; init; }
    public TrustAttributes Attributes { get; init; }
}

public sealed record DomainController
{
    /// <summary>DN of the DC's NTDS Settings object.</summary>
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? DnsHostName { get; init; }
    /// <summary>DNS name of the DC's domain; null if it couldn't be determined.</summary>
    public string? Domain { get; init; }
    public required string Site { get; init; }
    public bool IsGlobalCatalog { get; init; }
    public bool IsReadOnly { get; init; }
    public string? OperatingSystem { get; init; }
    public string? OperatingSystemVersion { get; init; }
    public IReadOnlyList<string> IPv4 { get; init; } = [];
}

public sealed record Site
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    /// <summary>options of the site's NTDS Site Settings.</summary>
    public int Options { get; init; }
    /// <summary>Inter-site topology generator: the DC key, if set.</summary>
    public string? Istg { get; init; }

    [JsonIgnore] public bool UniversalGroupCaching => (Options & 0x20) != 0;
}

public sealed record Subnet
{
    /// <summary>The subnet object's name, e.g. "10.1.0.0/24".</summary>
    public required string Prefix { get; init; }
    /// <summary>Site name; null if the subnet isn't associated with a site.</summary>
    public string? Site { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
}

public sealed record SiteLink
{
    public required string Name { get; init; }
    /// <summary>"IP" or "SMTP".</summary>
    public string Transport { get; init; } = "IP";
    public IReadOnlyList<string> Sites { get; init; } = [];
    public int Cost { get; init; }
    public int ReplicationIntervalMinutes { get; init; }
    public int Options { get; init; }
    /// <summary>Hours a week replication is allowed (of 168); null when the link has no schedule (always available).</summary>
    public int? ScheduleHours { get; init; }

    [JsonIgnore] public bool ChangeNotification => (Options & 0x1) != 0;
}

public sealed record SiteLinkBridge
{
    public required string Name { get; init; }
    public string Transport { get; init; } = "IP";
    public IReadOnlyList<string> SiteLinks { get; init; } = [];
}

/// <summary>An nTDSConnection: <see cref="To"/> replicates inbound from <see cref="From"/>.</summary>
public sealed record ReplicationConnection
{
    public required string Name { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public bool Enabled { get; init; } = true;
    /// <summary>Created by the KCC rather than by an administrator.</summary>
    public bool AutoGenerated { get; init; }
    /// <summary>"IP", "SMTP", or null for RPC (intra-site).</summary>
    public string? Transport { get; init; }
}
