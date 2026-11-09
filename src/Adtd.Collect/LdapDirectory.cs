using System.DirectoryServices.Protocols;
using System.Net;
using SdsScope = System.DirectoryServices.Protocols.SearchScope;

namespace Adtd.Collect;

/// <summary>Binds with Negotiate (Kerberos, else NTLM), with signing and sealing, and pages every search.</summary>
public sealed class LdapDirectoryFactory(ConnectionOptions options) : IDirectoryFactory
{
    public IDirectory Connect(string serverOrDomain, bool globalCatalog = false)
    {
        var port = globalCatalog ? 3268 : 389;
        var conn = new LdapConnection(new LdapDirectoryIdentifier(serverOrDomain, port, false, false), options.Credential, AuthType.Negotiate);
        try
        {
            conn.SessionOptions.ProtocolVersion = 3;
            conn.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            conn.SessionOptions.Signing = true;
            conn.SessionOptions.Sealing = true;
            conn.Timeout = options.EffectiveTimeout;
            conn.Bind();
            var server = serverOrDomain;
            return new LdapDirectory(conn, server, options.EffectiveTimeout);
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException)
        {
            conn.Dispose();
            throw new DirectoryUnavailableException(serverOrDomain, ex.Message, ex);
        }
    }
}

internal sealed class LdapDirectory(LdapConnection conn, string server, TimeSpan timeout) : IDirectory
{
    /// <summary>Attributes returned as raw bytes rather than strings.</summary>
    static readonly HashSet<string> BinaryAttributes = new(StringComparer.OrdinalIgnoreCase) { "schedule" };

    public string Server { get; } = server;

    public LdapEntry? Read(string dn, params string[] attributes)
    {
        try
        {
            var r = (SearchResponse)conn.SendRequest(new SearchRequest(dn, "(objectClass=*)", SdsScope.Base, attributes), timeout);
            return r.Entries.Count == 0 ? null : Convert(r.Entries[0], attributes);
        }
        catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject) { return null; }
    }

    public IReadOnlyList<LdapEntry> Search(string baseDn, string filter, Scope scope, params string[] attributes)
    {
        var result = new List<LdapEntry>();
        var request = new SearchRequest(baseDn, filter, scope switch { Scope.Base => SdsScope.Base, Scope.OneLevel => SdsScope.OneLevel, _ => SdsScope.Subtree }, attributes);
        var paging = new PageResultRequestControl(1000);
        request.Controls.Add(paging);
        try
        {
            while (true)
            {
                var r = (SearchResponse)conn.SendRequest(request, timeout);
                foreach (SearchResultEntry e in r.Entries) result.Add(Convert(e, attributes));
                var cookie = r.Controls.OfType<PageResultResponseControl>().FirstOrDefault()?.Cookie;
                if (cookie == null || cookie.Length == 0) break;
                paging.Cookie = cookie;
            }
        }
        catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject) { }
        return result;
    }

    static LdapEntry Convert(SearchResultEntry e, string[] attributes)
    {
        var values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var binary = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in e.Attributes.AttributeNames)
        {
            var a = e.Attributes[name];
            if (BinaryAttributes.Contains(name))
            {
                if (a.Count > 0 && a.GetValues(typeof(byte[])) is object[] { Length: > 0 } b) binary[name] = (byte[])b[0];
            }
            else values[name] = a.GetValues(typeof(string)).Cast<string>().ToArray();
        }
        return new LdapEntry(e.DistinguishedName, values, binary);
    }

    public void Dispose() => conn.Dispose();
}
