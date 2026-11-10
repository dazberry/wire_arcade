using System.Numerics;

namespace RelativeAsteroids;

/// <summary>A roving, shootable wireframe rock. Size 3 = large, 2 = medium, 1 = small.</summary>
sealed class Asteroid
{
    public static readonly float[] Radii = { 0, 16, 30, 54 };
    public static readonly int[] Points = { 0, 100, 50, 20 };

    public Vector2 Pos;
    public Vector2 Vel;
    public float Angle;
    public float Spin;
    public int Size;
    public Vector2[] Shape = Array.Empty<Vector2>();

    public float Radius => Radii[Size];

    public static Asteroid Create(Vector2 pos, Vector2 vel, int size, Random rng)
    {
        float radius = Radii[size];
        int verts = 10 + rng.Next(4);
        var shape = new Vector2[verts];
        for (int i = 0; i < verts; i++)
        {
            float a = i * MathF.Tau / verts;
            float r = radius * (0.72f + 0.34f * (float)rng.NextDouble());
            shape[i] = new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
        }

        return new Asteroid
        {
            Pos = pos,
            Vel = vel,
            Size = size,
            Shape = shape,
            Angle = (float)rng.NextDouble() * MathF.Tau,
            Spin = ((float)rng.NextDouble() - 0.5f) * 1.6f,
        };
    }
}

sealed class Bullet
{
    public Vector2 Pos;
    public Vector2 Vel;
    public float Life;
}

/// <summary>A drifting line fragment used for explosions.</summary>
sealed class Debris
{
    public Vector2 Pos;
    public Vector2 Vel;
    public float Angle;
    public float Spin;
    public float Length;
    public float Life;
    public float MaxLife;
    public bool Bright;
}
