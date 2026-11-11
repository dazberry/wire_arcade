using System.Numerics;

namespace WireGames;

/// <summary>
/// A wireframe 3D model: a bag of line segments in local coordinates
/// (x = right, y = up, z = forward). Used by the first-person 3D games.
/// </summary>
public sealed class Model
{
    public readonly List<(Vector3 A, Vector3 B)> Edges = new();

    /// <summary>Furthest horizontal distance of any vertex from the model's origin (used for culling).</summary>
    public float Radius { get; private set; }

    public Model Line(Vector3 a, Vector3 b)
    {
        Edges.Add((a, b));
        Radius = MathF.Max(Radius, MathF.Max(Horizontal(a), Horizontal(b)));
        return this;
    }

    static float Horizontal(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>A closed loop of line segments through the given points.</summary>
    public Model Loop(params Vector3[] pts)
    {
        for (int i = 0; i < pts.Length; i++)
            Line(pts[i], pts[(i + 1) % pts.Length]);
        return this;
    }

    /// <summary>A box-like solid whose top and bottom are rectangles of different sizes (so it can taper).</summary>
    public Model Frustum(float cx, float cz, float hx0, float hz0, float y0, float hx1, float hz1, float y1)
    {
        Vector3[] lo =
        {
            new(cx - hx0, y0, cz - hz0), new(cx + hx0, y0, cz - hz0), new(cx + hx0, y0, cz + hz0), new(cx - hx0, y0, cz + hz0),
        };
        Vector3[] hi =
        {
            new(cx - hx1, y1, cz - hz1), new(cx + hx1, y1, cz - hz1), new(cx + hx1, y1, cz + hz1), new(cx - hx1, y1, cz + hz1),
        };
        Loop(lo);
        Loop(hi);
        for (int i = 0; i < 4; i++)
            Line(lo[i], hi[i]);
        return this;
    }

    public Model Box(float cx, float cz, float hx, float hz, float y0, float y1) =>
        Frustum(cx, cz, hx, hz, y0, hx, hz, y1);

    /// <summary>A square tube running along z (a gun barrel or a missile body).</summary>
    public Model Tube(float x, float y, float half, float z0, float z1)
    {
        Vector3[] a =
        {
            new(x - half, y - half, z0), new(x + half, y - half, z0), new(x + half, y + half, z0), new(x - half, y + half, z0),
        };
        Vector3[] b =
        {
            new(x - half, y - half, z1), new(x + half, y - half, z1), new(x + half, y + half, z1), new(x - half, y + half, z1),
        };
        Loop(a);
        Loop(b);
        for (int i = 0; i < 4; i++)
            Line(a[i], b[i]);
        return this;
    }

    /// <summary>A horizontal ring of <paramref name="sides"/> segments.</summary>
    public Vector3[] Ring(float radius, float y, int sides) =>
        Polygon(new Vector3(0, y, 0), Vector3.UnitX, Vector3.UnitZ, radius, sides);

    /// <summary>
    /// A regular polygon lying in the plane spanned by the unit vectors <paramref name="u"/> and <paramref name="v"/>
    /// around <paramref name="centre"/>. Returns its vertices.
    /// </summary>
    public Vector3[] Polygon(Vector3 centre, Vector3 u, Vector3 v, float radius, int sides)
    {
        var pts = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * MathF.Tau / sides;
            pts[i] = centre + (u * MathF.Sin(a) + v * MathF.Cos(a)) * radius;
        }
        Loop(pts);
        return pts;
    }

    public Model Scaled(float k)
    {
        var m = new Model();
        foreach (var (a, b) in Edges)
            m.Line(a * k, b * k);
        return m;
    }
}
