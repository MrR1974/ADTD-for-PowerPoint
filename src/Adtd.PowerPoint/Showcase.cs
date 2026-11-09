using Adtd.Layout;

namespace Adtd.PowerPoint;

/// <summary>
/// A test slide using every kind of plan item, laid out in a layout's content area: theme swatches with contrasting
/// text, DC cards with badges, straight and elbow connectors glued to shapes, a hub, a table and a caption. Used to
/// check how a template takes the writer's output (<c>adtd template --showcase</c>).
/// </summary>
public static class Showcase
{
    public static DeckPlan Build(SlideFrame frame, ThemeInfo theme, double iconAspect, string layoutName)
    {
        var c = frame.Content;
        var items = new List<PlanItem>();
        var gap = c.W * 0.015;
        var tx = new ThemeColor(SchemeSlot.Tx1);

        // Swatches: the six accents, each with the text colour that reads best on it.
        var swH = c.H * 0.13;
        var swW = (c.W - 5 * gap) / 6;
        for (var i = 1; i <= 6; i++)
        {
            var fill = ThemeColor.Accent(i);
            items.Add(new BoxItem($"Accent {i}", new Rect(c.X + (i - 1) * (swW + gap), c.Y, swW, swH), Geometry.RoundRect)
            {
                Fill = fill,
                Text = PlanText.Lines([$"Accent {i}", $"#{theme.Resolve(fill)}"], 11, theme.TextOn(fill), boldFirst: true, HAlign.Center),
            });
        }

        // Two DC cards joined by a straight connector, on the left.
        var top = c.Y + swH + gap * 2;
        var cardW = c.W * 0.24;
        var cardH = c.H * 0.3;
        var card1 = Card("DC01", "dc01", new Rect(c.X, top, cardW, cardH), ThemeColor.Accent(1), ["GC", "PDC", "RID"], iconAspect, theme, false);
        var card2 = Card("DC02", "dc02", new Rect(c.X, top + cardH + gap * 3, cardW, cardH), ThemeColor.Accent(2), ["GC", "RODC"], iconAspect, theme, true);
        items.Add(card1);
        items.Add(card2);
        double x = c.X + cardW / 2;
        items.Add(new LinkItem("Replication DC01-DC02", "dc01", "dc02",
            [new Point(x, top + cardH), new Point(x, top + cardH + gap * 3)], new Stroke(tx, 1.5)) { Arrows = ArrowEnds.Both });

        // A hub with three sites, joined by elbow connectors, in the middle.
        var midX = c.X + cardW + gap * 4;
        var midW = c.W * 0.4;
        var siteW = midW * 0.28;
        var siteH = c.H * 0.13;
        var hub = new Rect(midX + midW / 2 - siteH * 0.6, top, siteH * 1.2, siteH * 1.2);
        items.Add(new BoxItem("Hub link", hub, Geometry.Ellipse)
        {
            Key = "hub", Fill = new ThemeColor(SchemeSlot.Bg1), Line = new Stroke(new ThemeColor(SchemeSlot.Tx2), 1.5),
            Text = PlanText.Simple("Hub", 9, new ThemeColor(SchemeSlot.Tx2), true, HAlign.Center) with { Inset = 0 },
        });
        var sitesY = top + hub.H + c.H * 0.14;
        for (var i = 0; i < 3; i++)
        {
            var fill = ThemeColor.Accent(i + 3).Lighter(0.6);
            var r = new Rect(midX + i * (midW - siteW) / 2, sitesY, siteW, siteH);
            items.Add(new BoxItem($"Site {i + 1}", r, Geometry.Rect)
            {
                Key = $"site{i}", Fill = fill, Line = new Stroke(ThemeColor.Accent(i + 3), 1),
                Text = PlanText.Simple($"Site {i + 1}", 11, theme.TextOn(fill), true, HAlign.Center),
            });
            var ys = hub.Bottom + (r.Y - hub.Bottom) / 2;
            items.Add(new LinkItem($"Link hub-site {i + 1}", "hub", $"site{i}",
                i == 1 ? [new Point(hub.CenterX, hub.Bottom), new Point(r.CenterX, r.Y)]
                       : [new Point(hub.CenterX, hub.Bottom), new Point(hub.CenterX, ys), new Point(r.CenterX, ys), new Point(r.CenterX, r.Y)],
                new Stroke(new ThemeColor(SchemeSlot.Tx2), 1.25, i == 2 ? Dash.Dash : Dash.Solid)) { Arrows = i == 0 ? ArrowEnds.End : ArrowEnds.None });
        }

        // A subnet table on the right, and a caption under the hub.
        var tableX = midX + midW + gap * 4;
        items.Add(new TableItem("Subnets", new Rect(tableX, top, c.Right - tableX, 0), ["Subnet", "Location"],
            [["10.0.0.0/24", "Sydney"], ["10.0.1.0/24", "Melbourne"], ["172.16.4.0/22", "Brisbane"], ["192.168.10.0/24", "Perth"]], 10)
        { ColumnWeights = [1.2, 1] });
        items.Add(new TextItem("Caption", new Rect(midX, sitesY + siteH + gap * 2, midW, c.Bottom - (sitesY + siteH + gap * 2)),
            PlanText.Lines([$"Layout: {layoutName}", $"Content area: {c}", $"Fonts: {theme.MajorFont} / {theme.MinorFont}"], 10, tx) with { Anchor = VAnchor.Top }));
        // The content area itself, dashed, so it can be checked against the template's artwork.
        items.Insert(0, new BoxItem("Content area", c, Geometry.Rect) { Line = new Stroke(new ThemeColor(SchemeSlot.Tx1, Alpha: 40000), 0.75, Dash.Dash) });
        return new DeckPlan([new SlidePlan($"Showcase: {layoutName}", items)]);
    }

    /// <summary>A DC card: outlined frame (the glue target), icon, name and details, and role badges.</summary>
    static GroupItem Card(string name, string key, Rect r, ThemeColor accent, string[] badges, double aspect, ThemeInfo theme, bool readOnly)
    {
        var pad = r.H * 0.08;
        var iconH = r.H - 2 * pad - r.H * 0.22;
        var icon = new Rect(r.X + pad, r.Y + pad, iconH * aspect, iconH);
        var textX = icon.Right + pad;
        var children = new List<PlanItem>
        {
            new BoxItem($"{name} frame", r, Geometry.RoundRect)
            {
                Key = key, Fill = new ThemeColor(SchemeSlot.Bg1), CornerRatio = 0.08,
                Line = new Stroke(accent, readOnly ? 1.5 : 2, readOnly ? Dash.Dash : Dash.Solid),
            },
            new IconItem($"{name} icon", icon),
            new TextItem($"{name} details", new Rect(textX, r.Y + pad, r.Right - pad - textX, iconH),
                PlanText.Lines([name, "10.0.0.4", "WS 2025 DC"], 10, new ThemeColor(SchemeSlot.Tx1), boldFirst: true) with { Anchor = VAnchor.Top, Inset = 0 }),
        };
        var bh = r.H * 0.16;
        var bw = (r.W - 2 * pad - (badges.Length - 1) * pad / 2) / Math.Max(4, badges.Length);
        for (var i = 0; i < badges.Length; i++)
        {
            var fill = badges[i] == "GC" ? accent.Darker(0.25) : new ThemeColor(SchemeSlot.Tx2);
            children.Add(new BoxItem($"{name} {badges[i]}", new Rect(r.X + pad + i * (bw + pad / 2), r.Bottom - pad - bh, bw, bh), Geometry.RoundRect)
            {
                Fill = badges[i] == "RODC" ? null : fill, CornerRatio = 0.5,
                Line = badges[i] == "RODC" ? new Stroke(accent, 1, Dash.Dash) : null,
                Text = PlanText.Simple(badges[i], 8, badges[i] == "RODC" ? accent : theme.TextOn(fill), true, HAlign.Center) with { Inset = 0 },
            });
        }
        return new GroupItem($"DC {name}", children);
    }
}
