using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Adtd.Collect;

public interface IIpResolver
{
    /// <summary>IPv4 addresses for each host name; hosts that don't resolve are left out.</summary>
    Task<IReadOnlyDictionary<string, string[]>> ResolveAsync(IReadOnlyCollection<string> hosts, CancellationToken ct);
}

/// <summary>Looks DCs up in DNS, a few at a time, giving up on any one after a timeout.</summary>
public sealed class DnsIpResolver(int parallelism = 8, TimeSpan? timeout = null) : IIpResolver
{
    public async Task<IReadOnlyDictionary<string, string[]>> ResolveAsync(IReadOnlyCollection<string> hosts, CancellationToken ct)
    {
        var result = new ConcurrentDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(hosts, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct }, async (host, token) =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cts.Token);
                var ips = addresses.Select(a => a.ToString()).Distinct().Order(StringComparer.Ordinal).ToArray();
                if (ips.Length > 0) result[host] = ips;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !token.IsCancellationRequested) { }
        });
        return result;
    }
}

/// <summary>The machine's own domain and forest, from the DC locator (DsGetDcName). Used to prefill the target.</summary>
[SupportedOSPlatform("windows")]
public static class DcLocator
{
    public sealed record Located(string DomainController, string Domain, string Forest, string? Site);

    /// <summary>Null when the machine isn't in a domain or no DC answers.</summary>
    public static Located? Find()
    {
        const uint DirectoryServiceRequired = 0x10, ReturnDnsName = 0x40000000;
        var rc = DsGetDcName(null, null, IntPtr.Zero, null, DirectoryServiceRequired | ReturnDnsName, out var ptr);
        if (rc != 0 || ptr == IntPtr.Zero) return null;
        try
        {
            var info = Marshal.PtrToStructure<DomainControllerInfo>(ptr);
            return new Located(info.DomainControllerName?.TrimStart('\\') ?? "", info.DomainName ?? "", info.DnsForestName ?? "", info.DcSiteName);
        }
        finally { NetApiBufferFree(ptr); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DomainControllerInfo
    {
        public string? DomainControllerName;
        public string? DomainControllerAddress;
        public uint DomainControllerAddressType;
        public Guid DomainGuid;
        public string? DomainName;
        public string? DnsForestName;
        public uint Flags;
        public string? DcSiteName;
        public string? ClientSiteName;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    static extern int DsGetDcName(string? computerName, string? domainName, IntPtr domainGuid, string? siteName, uint flags, out IntPtr info);

    [DllImport("netapi32.dll")]
    static extern int NetApiBufferFree(IntPtr buffer);
}
