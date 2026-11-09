namespace Adtd.Layout;

/// <summary>
/// How wide text is in the theme's fonts. The layout engine sizes every box from it, so a box holds its text on one
/// line at the chosen size. Adtd.Imaging measures with the real fonts; <see cref="ApproxTextMeasurer"/> estimates.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>The advance width of one line, EMU.</summary>
    double Width(string text, double sizePt, bool bold = false, FontRole font = FontRole.Minor);

    /// <summary>The height of one line at single spacing, EMU.</summary>
    double LineHeight(double sizePt, FontRole font = FontRole.Minor);

    /// <summary>Anything that makes the measurements less certain, such as a font that isn't installed.</summary>
    IReadOnlyList<string> Warnings => [];
}

/// <summary>
/// Estimates widths from character classes of a typical sans-serif font, slightly on the wide side. Deterministic and
/// font-free, for tests and for machines without the theme's fonts.
/// </summary>
public sealed class ApproxTextMeasurer : ITextMeasurer
{
    public static readonly ApproxTextMeasurer Instance = new();

    public double Width(string text, double sizePt, bool bold = false, FontRole font = FontRole.Minor)
    {
        double em = 0;
        foreach (var c in text) em += Em(c);
        return em * (bold ? 1.06 : 1) * sizePt * Emu.PerPoint;
    }

    public double LineHeight(double sizePt, FontRole font = FontRole.Minor) => sizePt * 1.2 * Emu.PerPoint;

    static double Em(char c) => c switch
    {
        ' ' or 'i' or 'l' or 'j' or '!' or '|' or '.' or ',' or ':' or ';' or '\'' => 0.27,
        'f' or 't' or 'r' or 'I' or '(' or ')' or '[' or ']' or '-' or '/' or '·' => 0.36,
        'm' or 'w' => 0.84,
        'M' or 'W' => 0.92,
        '…' or '—' or '↔' or '→' or '←' => 0.9,
        >= 'A' and <= 'Z' => 0.67,
        >= '0' and <= '9' => 0.56,
        _ => 0.54,
    };
}

/// <summary>Measuring helpers shared by the planners.</summary>
public static class TextFit
{
    /// <summary>The widest of <paramref name="lines"/>, EMU.</summary>
    public static double MaxWidth(this ITextMeasurer m, IEnumerable<string> lines, double sizePt, bool bold = false) =>
        lines.Select(l => m.Width(l, sizePt, bold)).DefaultIfEmpty(0).Max();

    /// <summary><paramref name="text"/>, shortened with an ellipsis if it's wider than <paramref name="maxWidth"/>.</summary>
    public static string Truncate(this ITextMeasurer m, string text, double maxWidth, double sizePt, bool bold = false)
    {
        if (m.Width(text, sizePt, bold) <= maxWidth) return text;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (m.Width(text[..mid].TrimEnd() + "…", sizePt, bold) <= maxWidth) lo = mid; else hi = mid - 1;
        }
        return lo == 0 ? "…" : text[..lo].TrimEnd() + "…";
    }

    /// <summary>
    /// Joins <paramref name="segments"/> with <paramref name="separator"/> into at most <paramref name="maxLines"/>
    /// lines of <paramref name="maxWidth"/>, breaking only between segments. Null if they don't fit.
    /// </summary>
    public static List<string>? Wrap(this ITextMeasurer m, IReadOnlyList<string> segments, string separator, double maxWidth, double sizePt, int maxLines)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var s in segments)
        {
            var candidate = current.Length == 0 ? s : current + separator + s;
            if (m.Width(candidate, sizePt) <= maxWidth) { current = candidate; continue; }
            if (current.Length == 0 || m.Width(s, sizePt) > maxWidth) return null;
            lines.Add(current);
            current = s;
        }
        if (current.Length > 0) lines.Add(current);
        return lines.Count <= maxLines ? lines : null;
    }
}
