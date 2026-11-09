using System.Text;

namespace Adtd.Collect;

/// <summary>Minimal distinguished-name handling: splitting on unescaped commas and reading RDN values.</summary>
public static class Dn
{
    /// <summary>The RDNs of a DN, still escaped ("CN=a\,b", "DC=com").</summary>
    public static List<string> Split(string dn)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < dn.Length; i++)
        {
            var c = dn[i];
            if (c == '\\' && i + 1 < dn.Length) { sb.Append(c).Append(dn[++i]); continue; }
            if (c == ',') { parts.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString().Trim());
        return parts;
    }

    public static string Parent(string dn)
    {
        var parts = Split(dn);
        return parts.Count <= 1 ? "" : string.Join(",", parts.Skip(1));
    }

    /// <summary>The unescaped value of the first RDN: "CN=Sydney,CN=Sites,..." → "Sydney".</summary>
    public static string Value(string dn) => RdnValue(Split(dn).FirstOrDefault() ?? "");

    /// <summary>The value of the RDN at <paramref name="index"/> (0 = leftmost).</summary>
    public static string ValueAt(string dn, int index)
    {
        var parts = Split(dn);
        return index < parts.Count ? RdnValue(parts[index]) : "";
    }

    /// <summary>"DC=emea,DC=contoso,DC=com" → "emea.contoso.com" (only the DC= components).</summary>
    public static string ToDns(string dn) =>
        string.Join('.', Split(dn).Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).Select(RdnValue));

    /// <summary>A DN AD has marked deleted (its RDN carries "\0ADEL:").</summary>
    public static bool IsDeleted(string dn) => dn.Contains("\\0ADEL:", StringComparison.OrdinalIgnoreCase) || dn.Contains("\nDEL:", StringComparison.Ordinal);

    static string RdnValue(string rdn)
    {
        var eq = rdn.IndexOf('=');
        var v = eq < 0 ? rdn : rdn[(eq + 1)..];
        var sb = new StringBuilder();
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] == '\\' && i + 1 < v.Length)
            {
                if (i + 2 < v.Length && Uri.IsHexDigit(v[i + 1]) && Uri.IsHexDigit(v[i + 2]))
                {
                    sb.Append((char)System.Convert.ToInt32(v.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else sb.Append(v[++i]);
            }
            else sb.Append(v[i]);
        }
        return sb.ToString();
    }
}

/// <summary>The schedule attribute of site links and connections (MS-ADTS 7.1.4.5.2, SCHEDULE structure).</summary>
public static class ScheduleBlob
{
    const int Header = 20, Hours = 168;

    /// <summary>Hours of the week (0–168) with at least one 15-minute slot open; null if the blob isn't readable.</summary>
    public static int? AvailableHours(byte[]? blob)
    {
        if (blob == null || blob.Length < Header) return null;
        var count = BitConverter.ToInt32(blob, 8);
        for (var i = 0; i < count; i++)
        {
            var at = 12 + i * 8;
            if (at + 8 > blob.Length) return null;
            var type = BitConverter.ToInt32(blob, at);
            var offset = BitConverter.ToInt32(blob, at + 4);
            if (type != 0 || offset + Hours > blob.Length) continue;
            return Enumerable.Range(offset, Hours).Count(j => (blob[j] & 0x0F) != 0);
        }
        return null;
    }

    /// <summary>A schedule with the first <paramref name="hours"/> hours of the week open. Used to build test data.</summary>
    public static byte[] Encode(int hours)
    {
        var b = new byte[Header + Hours];
        BitConverter.GetBytes(b.Length).CopyTo(b, 0);
        BitConverter.GetBytes(1).CopyTo(b, 8);       // one schedule
        BitConverter.GetBytes(0).CopyTo(b, 12);      // SCHEDULE_INTERVAL
        BitConverter.GetBytes(Header).CopyTo(b, 16); // data offset
        for (var i = 0; i < hours; i++) b[Header + i] = 0x0F;
        return b;
    }
}
