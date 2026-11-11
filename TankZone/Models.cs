using System.Numerics;
using WireGames;

namespace TankZone;

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
