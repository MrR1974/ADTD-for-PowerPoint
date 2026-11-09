using System.Globalization;

namespace Adtd.Layout;

/// <summary>
/// A template's theme as slides see it: each slot's colour after the master's colour map (so a dark template's Bg1
/// is dark), and its heading and body fonts.
/// </summary>
public sealed record ThemeInfo(string Name, IReadOnlyDictionary<SchemeSlot, string> Colors, string MajorFont, string MinorFont)
{
    /// <summary>Office's default theme, for tests and previews without a template.</summary>
    public static readonly ThemeInfo OfficeDefault = new("Office", new Dictionary<SchemeSlot, string>
    {
        [SchemeSlot.Tx1] = "000000", [SchemeSlot.Bg1] = "FFFFFF", [SchemeSlot.Tx2] = "0E2841", [SchemeSlot.Bg2] = "E8E8E8",
        [SchemeSlot.Accent1] = "156082", [SchemeSlot.Accent2] = "E97132", [SchemeSlot.Accent3] = "196B24",
        [SchemeSlot.Accent4] = "0F9ED5", [SchemeSlot.Accent5] = "A02B93", [SchemeSlot.Accent6] = "4EA72E", [SchemeSlot.Hlink] = "467886",
    }, "Aptos Display", "Aptos");

    /// <summary>The colour a reference stands for, as "RRGGBB".</summary>
    public string Resolve(ColorRef c) => c switch
    {
        RgbColor rgb => rgb.Hex.ToUpperInvariant(),
        ThemeColor t => ColorMath.Apply(Colors.GetValueOrDefault(t.Slot, "808080"), t.LumMod, t.LumOff),
        _ => "808080",
    };

    /// <summary>Tx1 or Bg1, whichever reads better on <paramref name="fill"/>.</summary>
    public ThemeColor TextOn(ColorRef fill)
    {
        var f = Resolve(fill);
        return ColorMath.Contrast(f, Resolve(new ThemeColor(SchemeSlot.Tx1))) >= ColorMath.Contrast(f, Resolve(new ThemeColor(SchemeSlot.Bg1)))
            ? new ThemeColor(SchemeSlot.Tx1) : new ThemeColor(SchemeSlot.Bg1);
    }
}

/// <summary>
/// Where a slide's content goes on the chosen layout: the slide size, the title placeholder (if any) and the content
/// rectangle the diagram must stay inside. All EMU.
/// </summary>
public sealed record SlideFrame(double Width, double Height, Rect Content, Rect? Title);

/// <summary>DrawingML colour arithmetic and WCAG contrast.</summary>
public static class ColorMath
{
    /// <summary>lumMod/lumOff as PowerPoint applies them: in HSL, L' = L × mod + off.</summary>
    public static string Apply(string hex, int lumMod, int lumOff)
    {
        if (lumMod == 100000 && lumOff == 0) return hex.ToUpperInvariant();
        var (h, s, l) = ToHsl(hex);
        l = Math.Clamp(l * lumMod / 100000.0 + lumOff / 100000.0, 0, 1);
        return FromHsl(h, s, l);
    }

    /// <summary>WCAG 2 contrast ratio, 1–21.</summary>
    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Lin(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    public static (double R, double G, double B) Rgb(string hex)
    {
        var v = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (((v >> 16) & 255) / 255.0, ((v >> 8) & 255) / 255.0, (v & 255) / 255.0);
    }

    static (double H, double S, double L) ToHsl(string hex)
    {
        var (r, g, b) = Rgb(hex);
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    static string FromHsl(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0) r = g = b = l;
        else
        {
            static double Hue(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            }
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3); g = Hue(p, q, h); b = Hue(p, q, h - 1.0 / 3);
        }
        return $"{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
    }
}
