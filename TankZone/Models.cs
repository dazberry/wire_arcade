using System.Numerics;

namespace TankZone;

/// <summary>
/// A wireframe model: a bag of line segments in local coordinates
/// (x = right, y = up, z = forward).
/// </summary>
sealed class Model
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
    public Vector3[] Ring(float radius, float y, int sides)
    {
        var pts = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * MathF.Tau / sides;
            pts[i] = new Vector3(MathF.Sin(a) * radius, y, MathF.Cos(a) * radius);
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

static class Models
{
    public static readonly Model Pyramid = MakePyramid(45f, 95f);
    public static readonly Model Cube = new Model().Box(0, 0, 36f, 36f, 0, 72f);
    public static readonly Model TallBox = new Model().Box(0, 0, 24f, 24f, 0, 120f);
    public static readonly Model Tank = MakeTank();
    public static readonly Model SuperTank = MakeSuperTank();
    public static readonly Model Missile = MakeMissile();
    public static readonly Model Saucer = MakeSaucer();

    static Model MakePyramid(float half, float height)
    {
        var m = new Model();
        Vector3[] baseRing =
        {
            new(-half, 0, -half), new(half, 0, -half), new(half, 0, half), new(-half, 0, half),
        };
        m.Loop(baseRing);
        var apex = new Vector3(0, height, 0);
        foreach (var p in baseRing)
            m.Line(p, apex);
        return m;
    }

    static Model MakeTank()
    {
        var m = new Model();
        m.Frustum(0, 0, 12, 17, 0, 9, 14, 8);                  // hull (tapers towards the top)
        m.Loop(new Vector3(12.5f, 0, -17), new Vector3(12.5f, 0, 17), new Vector3(12.5f, 5, 17), new Vector3(12.5f, 5, -17));    // tracks
        m.Loop(new Vector3(-12.5f, 0, -17), new Vector3(-12.5f, 0, 17), new Vector3(-12.5f, 5, 17), new Vector3(-12.5f, 5, -17));
        m.Frustum(0, -2, 7, 8, 8, 5, 6, 14);                   // turret
        m.Tube(0, 11, 1.8f, 6, 30);                            // gun barrel
        return m;
    }

    static Model MakeSuperTank()
    {
        var m = MakeTank().Scaled(1.35f);
        // extra: a sloped nose plate and a tall antenna
        m.Line(new Vector3(-9, 6, 22), new Vector3(0, 12, 26)).Line(new Vector3(9, 6, 22), new Vector3(0, 12, 26));
        m.Line(new Vector3(-4, 19, -8), new Vector3(-4, 40, -8));
        return m;
    }

    static Model MakeMissile()
    {
        var m = new Model();
        m.Tube(0, 6, 3f, -14, 8);
        var nose = new Vector3(0, 6, 22);
        foreach (var (x, y) in new[] { (-3f, 3f), (3f, 3f), (3f, 9f), (-3f, 9f) })
            m.Line(new Vector3(x, y, 8), nose);
        // tail fins
        m.Line(new Vector3(3, 6, -6), new Vector3(11, 6, -18)).Line(new Vector3(11, 6, -18), new Vector3(3, 6, -14));
        m.Line(new Vector3(-3, 6, -6), new Vector3(-11, 6, -18)).Line(new Vector3(-11, 6, -18), new Vector3(-3, 6, -14));
        m.Line(new Vector3(0, 9, -6), new Vector3(0, 17, -18)).Line(new Vector3(0, 17, -18), new Vector3(0, 9, -14));
        return m;
    }

    static Model MakeSaucer()
    {
        var m = new Model();
        const int n = 10;
        var rim = m.Ring(24f, 0, n);
        var dome = m.Ring(10f, 8f, n);
        var belly = m.Ring(9f, -6f, n);
        var apex = new Vector3(0, 16f, 0);
        for (int i = 0; i < n; i++)
        {
            m.Line(rim[i], dome[i]);
            m.Line(rim[i], belly[i]);
            if (i % 2 == 0)
                m.Line(dome[i], apex);
        }
        return m;
    }
}
