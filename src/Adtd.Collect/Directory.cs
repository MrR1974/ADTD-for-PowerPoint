using System.Net;

namespace Adtd.Collect;

public enum Scope { Base, OneLevel, Subtree }

/// <summary>One directory object: its DN and the requested attributes (attribute names are case-insensitive).</summary>
public sealed class LdapEntry(string dn, IReadOnlyDictionary<string, string[]> values, IReadOnlyDictionary<string, byte[]>? binary = null)
{
    public string Dn { get; } = dn;
    public IReadOnlyDictionary<string, string[]> Values { get; } = values;
    public IReadOnlyDictionary<string, byte[]> Binary { get; } = binary ?? new Dictionary<string, byte[]>();

    public string? Get(string attr) => Values.TryGetValue(attr, out var v) && v.Length > 0 ? v[0] : null;
    public string[] GetAll(string attr) => Values.TryGetValue(attr, out var v) ? v : [];
    public int GetInt(string attr, int fallback = 0) => int.TryParse(Get(attr), out var i) ? i : fallback;
    public bool GetBool(string attr, bool fallback) => Get(attr) is string s ? string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase) : fallback;
    public byte[]? GetBinary(string attr) => Binary.TryGetValue(attr, out var b) ? b : null;
}

/// <summary>The handful of LDAP operations the forest reader needs. Faked in tests.</summary>
public interface IDirectory : IDisposable
{
    /// <summary>The server actually bound to (a DC's DNS name).</summary>
    string Server { get; }
    /// <summary>A single object, or null if it doesn't exist. dn "" reads RootDSE.</summary>
    LdapEntry? Read(string dn, params string[] attributes);
    IReadOnlyList<LdapEntry> Search(string baseDn, string filter, Scope scope, params string[] attributes);
}

public interface IDirectoryFactory
{
    /// <summary>Binds to a DC: a server name, or a domain name for the locator to pick one. Throws <see cref="DirectoryUnavailableException"/>.</summary>
    IDirectory Connect(string serverOrDomain, bool globalCatalog = false);
}

public sealed class DirectoryUnavailableException(string target, string message, Exception? inner = null)
    : Exception($"Couldn't connect to {target}: {message}", inner)
{
    public string Target { get; } = target;
}

/// <summary>Who to bind as: null credential means the signed-in user.</summary>
public sealed record ConnectionOptions(NetworkCredential? Credential = null, TimeSpan? Timeout = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromSeconds(30);
}
