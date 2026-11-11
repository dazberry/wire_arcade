using System.Numerics;
using WireGames;

namespace TrenchRun;

/// <summary>Wireframe models. Local axes: x right, y up, z forward (the way the object "faces").</summary>
static class Models
{
    public static readonly Model Interceptor = MakeInterceptor();
    public static readonly Model DarkFighter = MakeDarkFighter();
    public static readonly Model Fireball = MakeFireball();
    public static readonly Model Bunker = MakeBunker();
    public static readonly Model TowerTop = new Model().Box(0, 0, 15f, 15f, -9f, 9f).Tube(0, 0, 2.5f, 0, 24f);
    public static readonly Model Turret = MakeTurret();
    public static readonly Model Port = MakePort();

    static Model MakeInterceptor()
    {
        var m = new Model();
        // cockpit ball: three crossed octagons
        m.Polygon(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 9f, 8);
        m.Polygon(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, 9f, 8);
        m.Polygon(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, 9f, 8);
        // two hexagonal solar panels on struts, with spokes
        foreach (float s in new[] { -1f, 1f })
        {
            var centre = new Vector3(s * 26f, 0, 0);
            var hex = m.Polygon(centre, Vector3.UnitY, Vector3.UnitZ, 26f, 6);
            foreach (var p in hex)
                m.Line(centre, p);
            m.Line(new Vector3(s * 9f, 0, 0), centre);
        }
        return m;
    }

    static Model MakeDarkFighter()
    {
        // The same design, bigger, with bent wings: extra struts sweep forward from the panels.
        var m = MakeInterceptor().Scaled(1.5f);
        foreach (float s in new[] { -1f, 1f })
        {
            m.Line(new Vector3(s * 39f, 39f, 0), new Vector3(s * 52f, 20f, 30f));
            m.Line(new Vector3(s * 39f, -39f, 0), new Vector3(s * 52f, -20f, 30f));
            m.Line(new Vector3(s * 52f, 20f, 30f), new Vector3(s * 52f, -20f, 30f));
        }
        return m;
    }

    static Model MakeFireball()
    {
        var m = new Model();
        m.Polygon(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 10f, 6);
        m.Polygon(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, 10f, 6);
        m.Polygon(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, 10f, 6);
        return m;
    }

    static Model MakeBunker()
    {
        var m = new Model();
        m.Frustum(0, 0, 28f, 28f, 0, 18f, 18f, 12f);      // low sloped base
        m.Box(0, 0, 9f, 9f, 12f, 22f);                    // gun housing
        m.Tube(0, 17f, 2.5f, 0, 30f);                     // barrel (pointing along +z)
        return m;
    }

    static Model MakeTurret()
    {
        var m = new Model();
        m.Box(0, 0, 11f, 11f, -8f, 8f);
        m.Tube(0, 0, 2.2f, 0, 24f);
        return m;
    }

    static Model MakePort()
    {
        var m = new Model();
        m.Polygon(new Vector3(0, 14f, 0), Vector3.UnitX, Vector3.UnitY, 18f, 8);
        m.Polygon(new Vector3(0, 14f, 0), Vector3.UnitX, Vector3.UnitY, 10f, 8);
        m.Polygon(new Vector3(0, 14f, 0), Vector3.UnitX, Vector3.UnitY, 4f, 6);
        return m;
    }

    /// <summary>A tall pole standing on the surface.</summary>
    public static Model TowerPole(float height) => new Model().Box(0, 0, 7f, 7f, 0, height);

    /// <summary>A beam spanning the whole trench, with a zig-zag truss.</summary>
    public static Model Catwalk(float halfWidth)
    {
        var m = new Model().Box(0, 0, halfWidth, 6f, -6f, 6f);
        const int n = 12;
        for (int i = 0; i < n; i++)
        {
            float x0 = -halfWidth + 2f * halfWidth * i / n;
            float x1 = -halfWidth + 2f * halfWidth * (i + 1) / n;
            m.Line(new Vector3(x0, i % 2 == 0 ? -6f : 6f, -6f), new Vector3(x1, i % 2 == 0 ? 6f : -6f, -6f));
        }
        return m;
    }
}
