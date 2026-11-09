using System.Xml;
using SkiaSharp;
using Svg.Skia;

namespace Adtd.Imaging;

/// <summary>
/// The domain controller icon: the original SVG, a PNG rendering of it (PowerPoint's fallback, and the app's preview),
/// and its aspect ratio. PNG icons are accepted too; they have no SVG.
/// </summary>
public sealed record IconAsset(string Name, byte[]? Svg, byte[] Png, double Aspect)
{
    /// <summary>Things about the SVG PowerPoint may not draw as expected.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The longest side of the PNG fallback, in pixels.</summary>
    public const int PngSize = 512;

    /// <summary>The icon the app ships with (assets/DomainController.svg).</summary>
    public static IconAsset Default()
    {
        using var s = typeof(IconAsset).Assembly.GetManifestResourceStream("DomainController.svg")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return FromSvg(ms.ToArray(), "DomainController.svg");
    }

    public static IconAsset Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var name = Path.GetFileName(path);
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? FromPng(bytes, name) : FromSvg(bytes, name);
    }

    public static IconAsset FromPng(byte[] png, string name)
    {
        using var bmp = SKBitmap.Decode(png) ?? throw new InvalidDataException($"{name} isn't a PNG image.");
        var warnings = Math.Max(bmp.Width, bmp.Height) < 256
            ? new[] { $"{name} is only {bmp.Width}×{bmp.Height} px; it will look soft on large slides. An SVG, or a PNG of 512 px or more, is better." }
            : [];
        return new IconAsset(name, null, png, bmp.Width / (double)bmp.Height) { Warnings = warnings };
    }

    /// <summary>
    /// Checks and renders an SVG. Scripts, foreign objects and external references are refused (PowerPoint ignores them,
    /// and an icon has no business carrying them); filters, CSS and text are allowed with a warning, because PowerPoint's
    /// SVG support is partial there.
    /// </summary>
    public static IconAsset FromSvg(byte[] svg, string name)
    {
        var warnings = new List<string>();
        Inspect(svg, name, warnings);

        using var doc = new SKSvg();
        using (var ms = new MemoryStream(svg)) doc.Load(ms);
        var pic = doc.Picture ?? throw new InvalidDataException($"{name} couldn't be drawn.");
        var b = pic.CullRect;
        if (b.Width <= 0 || b.Height <= 0) throw new InvalidDataException($"{name} has no size (it needs a viewBox or width and height).");

        var scale = PngSize / Math.Max(b.Width, b.Height);
        int w = Math.Max(1, (int)Math.Ceiling(b.Width * scale)), h = Math.Max(1, (int)Math.Ceiling(b.Height * scale));
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            c.Scale(scale);
            c.Translate(-b.Left, -b.Top);
            c.DrawPicture(pic);
        }
        using var png = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Png, 100);
        return new IconAsset(name, svg, png.ToArray(), b.Width / b.Height) { Warnings = warnings };
    }

    static void Inspect(byte[] svg, string name, List<string> warnings)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        try
        {
            using var r = XmlReader.Create(new MemoryStream(svg), settings);
            var root = true;
            var warned = new HashSet<string>();
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                if (root && r.LocalName != "svg") throw new InvalidDataException($"{name} isn't an SVG image.");
                root = false;
                switch (r.LocalName)
                {
                    case "script": throw new InvalidDataException($"{name} contains a script; icons can't.");
                    case "foreignObject": throw new InvalidDataException($"{name} contains embedded non-SVG content (foreignObject); icons can't.");
                    case "filter" when warned.Add("filter"): warnings.Add($"{name} uses SVG filters, which PowerPoint may not draw."); break;
                    case "style" when warned.Add("style"): warnings.Add($"{name} uses CSS styles, which PowerPoint may partly ignore."); break;
                    case "text" when warned.Add("text"): warnings.Add($"{name} contains text, which PowerPoint draws in its own fonts."); break;
                }
                for (var i = 0; i < r.AttributeCount; i++)
                {
                    r.MoveToAttribute(i);
                    if (r.LocalName == "href" && r.Value.Length > 0 && !r.Value.StartsWith('#') && !r.Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"{name} refers to an outside file ({r.Value}); icons must be self-contained.");
                }
                r.MoveToElement();
            }
        }
        catch (XmlException ex) { throw new InvalidDataException($"{name} isn't valid SVG: {ex.Message}", ex); }
    }
}
