using Adtd.Layout;
using SkiaSharp;

namespace Adtd.Imaging;

/// <summary>
/// Measures text in the theme's own fonts with SkiaSharp. Fonts are looked for among the installed fonts, then in
/// Office's cloud font cache (where Office keeps fonts such as Aptos that it downloads on demand). Widths carry 3%
/// slack for differences between Skia's and PowerPoint's text shaping; a font found in neither place is replaced by a
/// stand-in with a further 5% slack and a warning.
/// </summary>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    const double Slack = 1.03, MissingSlack = 1.05;
    readonly Dictionary<(FontRole, bool), (SKFont Font, double Slack)> _fonts = [];
    readonly List<string> _warnings = [];

    public SkiaTextMeasurer(ThemeInfo theme)
    {
        foreach (var role in new[] { FontRole.Minor, FontRole.Major })
        {
            var family = role == FontRole.Major ? theme.MajorFont : theme.MinorFont;
            var installed = SKFontManager.Default.MatchFamily(family) is { } probe && Dispose(probe);
            var cloud = installed ? [] : CloudFaces(family);
            if (!installed && cloud.Count == 0)
                _warnings.Add($"The theme font {family} isn't installed here; text is measured with a stand-in and boxes are sized 5% larger to allow for it.");
            foreach (var bold in new[] { false, true })
            {
                var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
                var face = installed ? SKTypeface.FromFamilyName(family, style)
                    : cloud.Count > 0 ? Closest(cloud, bold)
                    : SKTypeface.FromFamilyName("Segoe UI", style) ?? SKTypeface.Default;
                _fonts[(role, bold)] = (new SKFont(face, 100), installed || cloud.Count > 0 ? Slack : Slack * MissingSlack);
            }
            foreach (var unused in cloud.Where(f => _fonts.Values.All(v => v.Font.Typeface != f))) unused.Dispose();
        }
    }

    static bool Dispose(IDisposable d) { d.Dispose(); return true; }

    /// <summary>The upright faces of a family in Office's cloud font cache, if it has downloaded them.</summary>
    static List<SKTypeface> CloudFaces(string family)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "FontCache", "4", "CloudFonts", family);
        if (!Directory.Exists(dir)) return [];
        var faces = new List<SKTypeface>();
        foreach (var file in Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            var face = SKTypeface.FromFile(file);
            if (face == null) continue;
            if (string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase) && face.FontStyle.Slant == SKFontStyleSlant.Upright) faces.Add(face);
            else face.Dispose();
        }
        return faces;
    }

    static SKTypeface Closest(List<SKTypeface> faces, bool bold)
    {
        var target = bold ? 700 : 400;
        return faces.MinBy(f => Math.Abs(f.FontStyle.Weight - target) * 10 + Math.Abs(f.FontStyle.Width - 5))!;
    }

    public IReadOnlyList<string> Warnings => _warnings;

    public double Width(string text, double sizePt, bool bold = false, FontRole font = FontRole.Minor)
    {
        if (text.Length == 0) return 0;
        var (f, slack) = _fonts[(font, bold)];
        // Measured at 100 pt and scaled, so hinting at small sizes doesn't skew it.
        return f.MeasureText(text) / 100 * sizePt * slack * Emu.PerPoint;
    }

    public double LineHeight(double sizePt, FontRole font = FontRole.Minor)
    {
        var f = _fonts[(font, false)].Font;
        return Math.Max(1.2, f.Spacing / 100) * sizePt * Emu.PerPoint;
    }

    public void Dispose()
    {
        foreach (var face in _fonts.Values.Select(v => v.Font.Typeface).Distinct()) face.Dispose();
        foreach (var (f, _) in _fonts.Values) f.Dispose();
        _fonts.Clear();
    }
}
