// Ported from ADTD.Net (MIT, Goran Mihelcic) @ git 705e376^:
//   Mihelcic.Net.Visio/Arrange/Layouts/WebLayout.cs, Layouts/TreeNode.cs, Layouts/LayoutBase.cs (defaults), Arrange/Vector.cs.
// The geometry is ADTD.Net's, step for step: the spanning tree read from the node with most edges (Reorder/ReadTree),
// its balanced child order (BalanceTree/ReorderTree), radial initial placement (SetTreePosition), per-node relaxation
// (ArrangeNode) in two passes, CenterLayout, and the column of unconnected nodes to the right. Units are ADTD.Net's
// (Visio millimetres), with its constants: size factor 110, initial length 300, initial distance 0.3.
// One addition: ADTD.Net laid out only the component holding the busiest node and left the nodes of any other
// connected component at the origin; here each further component is laid out the same way and placed to the right.
namespace Adtd.Layout;

/// <summary>ADTD.Net's web layout for the sites diagram. Positions are node centres in millimetres, Y down.</summary>
public static class WebLayout
{
    const double InitialDistance = 0.3;   // INITIAL_DISTANCE
    const double SizeFactor = 110;        // _sizeFactor
    const double InitialLength = 300;     // LayoutBase._initial_Length
    const double Gap = 50;                // spacing of the unconnected column (and of further components)

    /// <param name="sizes">Each node's width and height, mm.</param>
    /// <param name="edges">Edges in the order they were added (ADTD.Net's order matters), as (from, to) node indexes.</param>
    public static Point[] Arrange(IReadOnlyList<(double W, double H)> sizes, IReadOnlyList<(int From, int To)> edges)
    {
        var all = sizes.Select((s, i) => new Node(i, s.W, s.H)).ToList();
        foreach (var (f, t) in edges)
        {
            if (f == t || f < 0 || t < 0 || f >= all.Count || t >= all.Count) continue;
            var e = new Edge(all[f], all[t]);
            all[f].Edges.Add(e);
            all[t].Edges.Add(e);
        }

        // Reorder: nodes by edge count (stable), the unconnected set aside.
        var available = all.OrderByDescending(n => n.Edges.Count).ToList();
        var disconnected = new List<Node>();
        if (available.Count > 1)
        {
            disconnected = available.Where(n => n.Edges.Count == 0).ToList();
            available.RemoveAll(n => n.Edges.Count == 0);
        }

        var components = new List<List<Node>>();
        while (available.Count > 0)
        {
            var first = available[0];
            available.RemoveAt(0);
            var read = new TreeNode(first);
            ReadTree(read, available);
            var tree = ReorderTree(read);
            var members = tree.Flat.Select(t => t.Node).ToList();
            var avg = members.Average(n => n.Size) * 2;   // _avgShapeSize
            first.X = 0; first.Y = 0;
            SetTreePosition(tree, 2 * Math.PI, 0, 0, avg);
            Relax(tree, false, members);
            Relax(tree, true, members);
            components.Add(members);
        }

        // CenterLayout for each component (ADTD.Net's margins are 0), further components to the right.
        double cursor = 0;
        foreach (var comp in components)
        {
            var minX = comp.Min(n => n.X - n.W / 2);
            var minY = comp.Min(n => n.Y - n.H / 2);
            foreach (var n in comp) { n.X -= minX - cursor; n.Y -= minY; }
            cursor = comp.Max(n => n.X + n.W / 2) + Gap;
        }

        // Unconnected nodes: a column right of the layout, from its bottom upwards.
        var laid = components.SelectMany(c => c).ToList();
        if (disconnected.Count > 0)
        {
            double x = laid.Count > 0 ? laid.Max(n => n.X + n.W / 2) : 0;
            double y = laid.Count > 0 ? laid.Max(n => n.Y + n.H / 2) : 0;
            foreach (var n in disconnected)
            {
                n.X = x + Gap;
                n.Y = y - n.H - Gap;
                y = n.Y;
            }
        }
        return all.Select(n => new Point(n.X, n.Y)).ToArray();
    }

    // ---- ADTD.Net's model -----------------------------------------------------------------------

    sealed class Node(int index, double w, double h)
    {
        public int Index { get; } = index;
        public double W { get; } = w;
        public double H { get; } = h;
        public double Size => Math.Max(W, H);
        public double X { get; set; }
        public double Y { get; set; }
        public List<Edge> Edges { get; } = [];
    }

    sealed record Edge(Node From, Node To);

    sealed class TreeNode(Node node)
    {
        public Node Node { get; } = node;
        public TreeNode? Parent { get; private set; }
        public List<TreeNode> Children { get; } = [];
        public double SubRadius { get; set; }
        public int Level => Parent == null ? 0 : Parent.Level + 1;
        public int Count => Children.Sum(c => c.Count) + 1;

        public TreeNode AddChild(Node n) { var c = new TreeNode(n); AddChild(c); return c; }
        public void AddChild(TreeNode c) { Children.Add(c); c.Parent = this; }

        public IEnumerable<TreeNode> Flat => Children.SelectMany(c => c.Flat).Prepend(this);

        TreeNode? Left
        {
            get
            {
                if (Parent == null || Parent.Children.Count == 1) return null;
                var n = Parent.Children.IndexOf(this);
                return n == 0 ? Parent.Children[^1] : Parent.Children[n - 1];
            }
        }

        TreeNode? Right
        {
            get
            {
                if (Parent == null || Parent.Children.Count == 1) return null;
                var n = Parent.Children.IndexOf(this);
                return n == Parent.Children.Count - 1 ? Parent.Children[0] : Parent.Children[n + 1];
            }
        }

        /// <summary>How many neighbours on the left have no subtree to speak of.</summary>
        public int LeftFree => Free(t => t.Left);
        public int RightFree => Free(t => t.Right);

        int Free(Func<TreeNode, TreeNode?> step)
        {
            if (Parent == null || Parent.Children.Count == 1 || Children.Count < 2) return 0;
            var result = 0;
            var next = step(this)!;
            for (var i = 0; i < Parent.Children.Count / 2; i++)
            {
                if (next.Children.Count == 0) result++;
                else if (next.Children.Count == 1) { result++; break; }
                else break;
                next = step(next)!;
            }
            return result;
        }
    }

    /// <summary>ADTD.Net's Vector, with its own direction convention.</summary>
    readonly record struct Vec(double X, double Y)
    {
        public static readonly Vec Null = new(0, 0);
        public double Magnitude => Math.Sqrt(X * X + Y * Y);

        public double Direction
        {
            get
            {
                double r;
                if (Y == 0) r = 0;
                else r = X == 0 ? Math.PI / 2 : Math.Atan(Y / X);
                if (X < 0) r = Math.PI - Math.Abs(r);
                if (Y < 0) r = -Math.Abs(r);
                return r;
            }
        }

        public static Vec Polar(double magnitude, double direction) => new(magnitude * Math.Cos(direction), magnitude * Math.Sin(direction));
        public static Vec Between(Node a, Node b) => new(b.X - a.X, b.Y - a.Y);
        public Vec Invert() => new(-X, -Y);
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    }

    // ---- Reorder ------------------------------------------------------------------------------------

    static void ReadTree(TreeNode t, List<Node> available)
    {
        foreach (var e in t.Node.Edges.OrderByDescending(e => e.From.Edges.Count + e.To.Edges.Count))
        {
            var other = e.From == t.Node ? e.To : e.From;
            if (available.Remove(other)) t.AddChild(other);
        }
        foreach (var c in t.Children.ToList()) ReadTree(c, available);
    }

    static TreeNode ReorderTree(TreeNode original)
    {
        var result = new TreeNode(original.Node);
        var balanced = Balance(original.Children).ToArray();
        var n = balanced.Length / 2;
        var reordered = balanced.Skip(n).Concat(balanced.Take(n));
        foreach (var sub in reordered) result.AddChild(ReorderTree(sub));
        return result;
    }

    static IEnumerable<TreeNode> Balance(List<TreeNode> nodes)
    {
        var n = nodes.Count;
        for (var i = 0; i < n; i++)
            yield return i % 2 == 0 ? nodes[i / 2] : nodes[n - i / 2 - 1];
    }

    // ---- SetTreePosition ----------------------------------------------------------------------------

    static void SetTreePosition(TreeNode node, double alpha, double radius, double direction, double avg)
    {
        var n = node.Children.Count;
        if (n > 1)
        {
            if (node.Level > 0)
            {
                var leftExtension = Math.Min(alpha * node.LeftFree, Math.PI / 3);
                var rightExtension = Math.Min(alpha * node.RightFree, Math.PI / 3);
                if (leftExtension > 0 || rightExtension > 0)
                {
                    direction = direction - leftExtension + rightExtension;
                    alpha = Math.Min(alpha + leftExtension + rightExtension, Math.PI);
                }
            }
            node.SubRadius = Math.Max(n * avg * InitialDistance / alpha, InitialLength);

            var d = Math.Sin(alpha / 2) * radius;
            var gama = Math.Abs(Math.PI / 2 - alpha / 2);
            var gama2 = d > node.SubRadius ? 0 : Math.Acos(d / node.SubRadius);
            var beta = node.Level == 0 ? Math.PI * 2 : Math.Min((Math.PI - gama - gama2) * 2, Math.PI);
            var i = 0;
            var subbeta = beta / n;
            var startDirection = direction - beta / 2 + subbeta / 2;
            foreach (var sub in node.Children)
            {
                direction = startDirection + subbeta * i;
                var magnitude = node.SubRadius;
                if (sub.Children.Count > 10)
                {
                    subbeta = 2 * Math.PI / 3;
                    magnitude = Math.Max(sub.Children.Count * avg * InitialDistance / subbeta, InitialLength);
                }
                else if (sub.Count > 1)
                    magnitude = node.SubRadius + InitialLength;
                var move = Vec.Polar(magnitude, direction);
                sub.Node.X = node.Node.X + move.X;
                sub.Node.Y = node.Node.Y + move.Y;
                SetTreePosition(sub, subbeta, magnitude, direction, avg);
                i++;
            }
        }
        else if (n == 1)
        {
            var sub = node.Children[0];
            node.SubRadius = InitialLength;
            var move = Vec.Polar(node.SubRadius, direction);
            sub.Node.X = node.Node.X + move.X;
            sub.Node.Y = node.Node.Y + move.Y;
            SetTreePosition(sub, alpha, node.SubRadius, direction, avg);
        }
        else
            node.SubRadius = radius == 0 ? SizeFactor : radius;
    }

    // ---- ArrangeNode ---------------------------------------------------------------------------------

    static void Relax(TreeNode t, bool extended, List<Node> container)
    {
        ArrangeNode(t.Node, extended, t.SubRadius, container);
        foreach (var c in t.Children) Relax(c, extended, container);
    }

    static void ArrangeNode(Node node, bool extended, double sizeFactor, List<Node> container)
    {
        Vec resultVector, previousMove = Vec.Null, attraction = Vec.Null, repulsion = Vec.Null, repulsion2 = Vec.Null;
        var previousPosition = Vec.Null;   // as in ADTD.Net: never updated, so an overshoot steps back from the origin
        var i = 0;
        var connected = node.Edges.Select(e => e.To == node ? e.From : e.To).ToHashSet();
        do
        {
            var others = node.Edges.Where(e => e.From == node).Select(e => e.To).Concat(node.Edges.Where(e => e.To == node).Select(e => e.From)).ToList();
            if (others.Count > 0)
            {
                attraction = Vec.Null;
                repulsion = Vec.Null;
                foreach (var o in others)
                {
                    var rel = Vec.Between(node, o);
                    attraction += AsAttraction(rel, sizeFactor);
                    repulsion += AsRepulsion(rel, sizeFactor);
                }
            }
            if (extended)
            {
                var sum = Vec.Null;
                var any = false;
                foreach (var o in container)
                {
                    if (o == node || connected.Contains(o)) continue;
                    sum += AsRepulsion2(Vec.Between(node, o));
                    any = true;
                }
                if (any) repulsion2 = sum;
            }
            resultVector = repulsion + attraction + repulsion2;

            Vec result;
            if ((int)resultVector.Magnitude == (int)previousMove.Magnitude) break;
            if (i > 0 && Convert.ToInt32(previousMove.Invert().Direction) == Convert.ToInt32(resultVector.Direction))
            {
                resultVector = Vec.Polar(previousMove.Magnitude / 2, previousMove.Direction);
                result = previousPosition + resultVector;
            }
            else
                result = new Vec(node.X, node.Y) + Vec.Polar(Math.Min(SizeFactor / 50, resultVector.Magnitude), resultVector.Direction);
            previousMove = resultVector;
            node.X = result.X;
            node.Y = result.Y;
            i++;
        } while (resultVector.Magnitude > SizeFactor / 256 && i < 1000);
    }

    static Vec AsAttraction(Vec relation, double factor) =>
        Vec.Polar(Math.Pow(relation.Magnitude, 3) / Math.Pow(factor, 3), relation.Direction);

    static Vec AsRepulsion(Vec relation, double factor)
    {
        relation = relation.Invert();
        return Vec.Polar(Math.Pow(factor, 3) / Math.Pow(Math.Max(1, relation.Magnitude), 3), relation.Direction);
    }

    static Vec AsRepulsion2(Vec relation)
    {
        relation = relation.Invert();
        var distance = relation.Magnitude;
        return Vec.Polar(distance < SizeFactor ? SizeFactor - distance : 0, relation.Direction);
    }
}
