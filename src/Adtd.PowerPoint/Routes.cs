// Adapted from Recast's src/Recast.PowerPoint/SlideBuilder.Connectors.cs (MatchRoute).
using Adtd.Layout;
using static Adtd.PowerPoint.Xml;

namespace Adtd.PowerPoint;

internal static class Routes
{
    /// <summary>
    /// The connector preset and placement that draw exactly <paramref name="pts"/>: straightConnector1 for two points,
    /// bentConnector2–5 for 2–5 alternating horizontal/vertical segments. PowerPoint's connectors in their own frame
    /// run from (0,0) to (w,h), the first segment along +x (bentConnector3 is (0,0)→(x1,0)→(x1,h)→(w,h)); a route
    /// matches when some rotation (multiple of 90°) and mirroring of that frame puts every vertex on it.
    /// Null if no preset matches.
    /// </summary>
    public static (string Geometry, string Xfrm)? Match(IReadOnlyList<Point> route)
    {
        const double tol = 2;   // EMU
        var pts = new List<Point> { route[0] };
        foreach (var p in route.Skip(1))
            if (Math.Abs(p.X - pts[^1].X) >= 1 || Math.Abs(p.Y - pts[^1].Y) >= 1) pts.Add(p);
        if (pts.Count < 2) pts.Add(pts[0]);
        var n = pts.Count - 1;
        if (n == 1)
        {
            var (s, e) = (pts[0], pts[1]);
            var flip = (e.X < s.X ? " flipH=\"1\"" : "") + (e.Y < s.Y ? " flipV=\"1\"" : "");
            return ("<a:prstGeom prst=\"straightConnector1\"><a:avLst/></a:prstGeom>",
                    Xfrm(Math.Min(s.X, e.X), Math.Min(s.Y, e.Y), Math.Abs(e.X - s.X), Math.Abs(e.Y - s.Y), 0, flip));
        }
        if (n > 5) return null;
        for (var i = 0; i < n; i++)
        {
            bool h = Math.Abs(pts[i + 1].Y - pts[i].Y) < tol, v = Math.Abs(pts[i + 1].X - pts[i].X) < tol;
            if (!h && !v) return null;
        }

        foreach (var rot in new[] { 0, 90, 180, 270 })
            foreach (var flipH in new[] { false, true })
            {
                var (cos, sin) = rot switch { 0 => (1, 0), 90 => (0, 1), 180 => (-1, 0), _ => (0, -1) };
                var f = flipH ? -1 : 1;
                (double X, double Y) Inv(double dx, double dy) => (f * (cos * dx + sin * dy), -sin * dx + cos * dy);
                var (s, e) = (pts[0], pts[^1]);
                var (w, hgt) = Inv(e.X - s.X, e.Y - s.Y);
                if (w < -tol || hgt < -tol) continue;
                double cx = (s.X + e.X) / 2, cy = (s.Y + e.Y) / 2;
                var local = pts.Select(p => { var q = Inv(p.X - cx, p.Y - cy); return (X: q.X + w / 2, Y: q.Y + hgt / 2); }).ToList();
                if (Math.Abs(local[1].Y) > tol) continue;

                List<(double X, double Y)> expect;
                var adj = new List<double>();
                double Frac(double v, double size) => size > tol ? v / size * 100000 : double.NaN;
                switch (n)
                {
                    case 2: expect = [(0, 0), (w, 0), (w, hgt)]; break;
                    case 3: { var x1 = local[1].X; adj.Add(Frac(x1, w)); expect = [(0, 0), (x1, 0), (x1, hgt), (w, hgt)]; break; }
                    case 4:
                        {
                            double x1 = local[1].X, y2 = local[2].Y; adj.Add(Frac(x1, w)); adj.Add(Frac(y2, hgt));
                            expect = [(0, 0), (x1, 0), (x1, y2), (w, y2), (w, hgt)]; break;
                        }
                    default:
                        {
                            double x1 = local[1].X, y2 = local[2].Y, x3 = local[3].X; adj.Add(Frac(x1, w)); adj.Add(Frac(y2, hgt)); adj.Add(Frac(x3, w));
                            expect = [(0, 0), (x1, 0), (x1, y2), (x3, y2), (x3, hgt), (w, hgt)]; break;
                        }
                }
                if (adj.Any(double.IsNaN)) continue;
                if (local.Zip(expect).Any(z => Math.Abs(z.First.X - z.Second.X) > tol || Math.Abs(z.First.Y - z.Second.Y) > tol)) continue;
                var av = string.Concat(adj.Select((a, i) => $"<a:gd name=\"adj{i + 1}\" fmla=\"val {N(a)}\"/>"));
                return ($"<a:prstGeom prst=\"bentConnector{n}\"><a:avLst>{av}</a:avLst></a:prstGeom>",
                        Xfrm(cx - w / 2, cy - hgt / 2, w, hgt, rot, flipH ? " flipH=\"1\"" : ""));
            }
        return null;
    }

    static string Xfrm(double x, double y, double w, double h, int rot, string flips) =>
        $"<a:xfrm{(rot != 0 ? $" rot=\"{rot * 60000}\"" : "")}{flips}><a:off x=\"{N(x)}\" y=\"{N(y)}\"/><a:ext cx=\"{N(Math.Max(0, w))}\" cy=\"{N(Math.Max(0, h))}\"/></a:xfrm>";
}
