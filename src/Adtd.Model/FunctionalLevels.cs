namespace Adtd.Model;

/// <summary>Display names for AD version numbers. Ported from ADTD.Net's DomainDataReader, extended to 2025.</summary>
public static class FunctionalLevels
{
    /// <summary>Forest or domain functional level (msDS-Behavior-Version).</summary>
    public static string Name(int level) => level switch
    {
        0 => "Windows 2000",
        1 => "Windows Server 2003 interim",
        2 => "Windows Server 2003",
        3 => "Windows Server 2008",
        4 => "Windows Server 2008 R2",
        5 => "Windows Server 2012",
        6 => "Windows Server 2012 R2",
        7 => "Windows Server 2016",
        10 => "Windows Server 2025",
        _ => $"Unknown ({level})",
    };

    /// <summary>Schema objectVersion.</summary>
    public static string SchemaName(int version) => version switch
    {
        13 => "Windows 2000",
        30 => "Windows Server 2003",
        31 => "Windows Server 2003 R2",
        44 => "Windows Server 2008",
        47 => "Windows Server 2008 R2",
        56 => "Windows Server 2012",
        69 => "Windows Server 2012 R2",
        87 => "Windows Server 2016",
        88 => "Windows Server 2019/2022",
        91 => "Windows Server 2025",
        _ => $"Unknown ({version})",
    };
}

/// <summary>Short operating system labels for DC cards. Follows ADTD.Net's OsTranslator.</summary>
public static class OsNames
{
    public static string Short(string? os)
    {
        if (string.IsNullOrWhiteSpace(os)) return "";
        var s = os.Replace("Windows Server®", "WS").Replace("Windows Server", "WS")
                  .Replace("Standard", "Std").Replace("Datacenter", "DC").Replace("Enterprise", "Ent")
                  .Replace("Evaluation", "Eval").Replace("Service Pack ", "SP")
                  .Replace("Azure Edition", "Azure");
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
