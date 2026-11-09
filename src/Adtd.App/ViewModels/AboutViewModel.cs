using System.IO;
using System.Reflection;

namespace Adtd.App.ViewModels;

/// <summary>The About page: the version, ADTD.Net's attribution and the third-party notices (NOTICE.md).</summary>
public sealed class AboutViewModel
{
    public string Version { get; } = Strings.F("About_Version",
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0");

    public string Notices { get; } = ReadNotices();

    static string ReadNotices()
    {
        using var s = typeof(AboutViewModel).Assembly.GetManifestResourceStream("NOTICE.md");
        if (s == null) return "";
        using var r = new StreamReader(s);
        return PlainText(r.ReadToEnd());
    }

    /// <summary>NOTICE.md as plain text for a TextBlock: headings in capitals, quotes unmarked, table rows as bullets.</summary>
    public static string PlainText(string markdown)
    {
        var lines = new List<string>();
        var header = true;
        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var l = raw.Replace("**", "");
            if (l.StartsWith('#')) { lines.Add(l.TrimStart('#', ' ').ToUpperInvariant()); continue; }
            if (l == ">") { lines.Add(""); continue; }
            if (l.StartsWith("> ")) { lines.Add(l[2..]); continue; }
            if (l.StartsWith('|'))
            {
                if (l.StartsWith("|---")) continue;
                var cells = l.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (header) { header = false; continue; }
                lines.Add($"•  {cells[0]}: {string.Join(", ", cells.Skip(1))}");
                continue;
            }
            lines.Add(l);
        }
        // Rejoin wrapped paragraphs so the TextBlock can wrap them to its own width.
        var paras = new List<string>();
        foreach (var l in lines)
        {
            var plain = l.Length > 0 && !l.StartsWith('•') && l != l.ToUpperInvariant();
            if (plain && paras.Count > 0 && paras[^1].Length > 0 && !paras[^1].StartsWith('•') && paras[^1] != paras[^1].ToUpperInvariant())
                paras[^1] += " " + l;
            else paras.Add(l);
        }
        return string.Join("\n", paras).Trim();
    }
}
