using System.Numerics;
using System.Drawing;
using System.Windows.Forms;

using WireGames;

namespace RelativeAsteroids;

/// <summary>
/// Asteroids with a ship-centric camera.
///
/// The ship is always drawn at the centre of the screen, pointing straight up. The simulation
/// still keeps world-space positions (ship, asteroids, bullets all live on a wrapping torus),
/// but every object is rendered *relative to the ship*, then the whole picture is rotated by
/// the inverse of the ship's heading. So turning rotates the world, and thrusting makes the
/// world slide past.
/// </summary>
public sealed class Game : IGame
{
    // World is a torus of World x World units. It must be larger than the screen diagonal so
    // that an object is never visible twice (we always draw the copy nearest to the ship).
    const float World = 1800f;
    const float Grid = 150f; // must divide World evenly so the marker lattice wraps seamlessly

    // Ship tuning
    const float TurnRate = 3.4f;       // rad/s
    const float Thrust = 260f;         // units/s^2
    const float ReverseThrust = 120f;
    const float MaxSpeed = 480f;
    const float Drag = 0.12f;          // per second; a little friction keeps it controllable
    const float ShipRadius = 11f;
    const float NoseOffset = 18f;

    // Weapon tuning
    const float BulletSpeed = 620f;
    const float BulletLife = 1.15f;
    const float FireDelay = 0.17f;
    const int MaxBullets = 6;

    static readonly Color Green = Color.FromArgb(255, 120, 255, 150);
    static readonly Color Amber = Color.FromArgb(255, 255, 190, 80);
    static readonly Color Rock = Color.FromArgb(255, 225, 235, 255);
    static readonly Color Dim = Color.FromArgb(255, 70, 90, 80);

    readonly Random _rng = new();
    readonly List<Asteroid> _asteroids = new();
    readonly List<Bullet> _bullets = new();
    readonly List<Debris> _debris = new();

    // Ship: world position + velocity + heading. Heading 0 = world "up"; positive = clockwise.
    Vector2 _shipPos;
    Vector2 _shipVel;
    float _heading;
    bool _shipAlive;
    bool _thrusting;
    float _respawnTimer;
    float _invulnerable;
    float _fireCooldown;

    int _score;
    int _lives;
    int _wave;
    bool _gameOver;
    bool _paused;
    float _time;

    Vector2 _center;

    public Game() => Reset();

    // ------------------------------------------------------------------ setup

    void Reset()
    {
        _asteroids.Clear();
        _bullets.Clear();
        _debris.Clear();

        _shipPos = new Vector2(World / 2, World / 2);
        _shipVel = Vector2.Zero;
        _heading = 0;
        _shipAlive = true;
        _invulnerable = 2f;
        _respawnTimer = 0;
        _fireCooldown = 0;

        _score = 0;
        _lives = 3;
        _wave = 0;
        _gameOver = false;
        _paused = false;

        StartNextWave();
    }

    void StartNextWave()
    {
        _wave++;
        int count = Math.Min(5 + _wave, 12);
        for (int i = 0; i < count; i++)
        {
            Vector2 pos;
            do
            {
                pos = new Vector2(Rand() * World, Rand() * World);
            } while (Rel(pos).Length() < 450f); // keep the start area clear

            float speed = 30f + Rand() * (35f + _wave * 6f);
            _asteroids.Add(Asteroid.Create(pos, RandomDir() * speed, 3, _rng));
        }
    }

    // ----------------------------------------------------------------- helpers

    float Rand() => (float)_rng.NextDouble();

    Vector2 RandomDir()
    {
        float a = Rand() * MathF.Tau;
        return new Vector2(MathF.Cos(a), MathF.Sin(a));
    }

    static Vector2 Forward(float heading) => new(MathF.Sin(heading), -MathF.Cos(heading));

    static Vector2 Rot(Vector2 v, float a)
    {
        float c = MathF.Cos(a), s = MathF.Sin(a);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    static float WrapCoord(float v) => v - World * MathF.Floor(v / World);

    static Vector2 WrapPos(Vector2 p) => new(WrapCoord(p.X), WrapCoord(p.Y));

    /// <summary>Shortest wrapped offset (each axis in [-World/2, World/2)).</summary>
    static Vector2 WrapDelta(Vector2 d) =>
        new(d.X - World * MathF.Floor(d.X / World + 0.5f), d.Y - World * MathF.Floor(d.Y / World + 0.5f));

    /// <summary>Where a world position is relative to the ship, in unrotated world axes.</summary>
    Vector2 Rel(Vector2 worldPos) => WrapDelta(worldPos - _shipPos);

    /// <summary>Ship-relative offset -> screen position (rotate by -heading so the ship faces up).</summary>
    Vector2 View(Vector2 rel) => _center + Rot(rel, -_heading);

    // ------------------------------------------------------------------ update

    public void Update(float dt)
    {
        if (Input.Pressed(Keys.P) && !_gameOver)
            _paused = !_paused;
        if (_gameOver && Input.Pressed(Keys.Enter))
        {
            Reset();
            return;
        }
        if (_paused)
            return;

        _time += dt;

        UpdateShip(dt);
        UpdateBullets(dt);
        UpdateAsteroids(dt);
        UpdateDebris(dt);
        HandleCollisions();

        if (_asteroids.Count == 0 && !_gameOver)
            StartNextWave();
    }

    void UpdateShip(float dt)
    {
        _thrusting = false;
        _fireCooldown = MathF.Max(0, _fireCooldown - dt);

        if (_shipAlive)
        {
            _invulnerable = MathF.Max(0, _invulnerable - dt);

            float turn = 0;
            if (Input.Down(Keys.Left) || Input.Down(Keys.A)) turn -= 1;
            if (Input.Down(Keys.Right) || Input.Down(Keys.D)) turn += 1;
            _heading += turn * TurnRate * dt;

            Vector2 fwd = Forward(_heading);
            if (Input.Down(Keys.Up) || Input.Down(Keys.W))
            {
                _shipVel += fwd * Thrust * dt;
                _thrusting = true;
            }
            if (Input.Down(Keys.Down) || Input.Down(Keys.S))
                _shipVel -= fwd * ReverseThrust * dt;

            if (Input.Down(Keys.Space) && _fireCooldown <= 0 && _bullets.Count < MaxBullets)
            {
                _bullets.Add(new Bullet
                {
                    Pos = _shipPos + fwd * NoseOffset,
                    Vel = _shipVel + fwd * BulletSpeed,
                    Life = BulletLife,
                });
                _fireCooldown = FireDelay;
            }
        }
        else if (!_gameOver)
        {
            _respawnTimer -= dt;
            if (_respawnTimer <= 0)
            {
                _shipAlive = true;
                _shipVel = Vector2.Zero;
                _invulnerable = 3f;
            }
        }

        _shipVel *= MathF.Exp(-Drag * dt);
        float speed = _shipVel.Length();
        if (speed > MaxSpeed)
            _shipVel *= MaxSpeed / speed;

        _shipPos = WrapPos(_shipPos + _shipVel * dt);
    }

    void UpdateBullets(float dt)
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            b.Pos = WrapPos(b.Pos + b.Vel * dt);
            b.Life -= dt;
            if (b.Life <= 0)
                _bullets.RemoveAt(i);
        }
    }

    void UpdateAsteroids(float dt)
    {
        foreach (var a in _asteroids)
        {
            a.Pos = WrapPos(a.Pos + a.Vel * dt);
            a.Angle += a.Spin * dt;
        }
    }

    void UpdateDebris(float dt)
    {
        for (int i = _debris.Count - 1; i >= 0; i--)
        {
            var d = _debris[i];
            d.Pos = WrapPos(d.Pos + d.Vel * dt);
            d.Angle += d.Spin * dt;
            d.Life -= dt;
            if (d.Life <= 0)
                _debris.RemoveAt(i);
        }
    }

    void HandleCollisions()
    {
        var spawned = new List<Asteroid>();

        // Bullets vs asteroids
        for (int bi = _bullets.Count - 1; bi >= 0; bi--)
        {
            var b = _bullets[bi];
            for (int ai = _asteroids.Count - 1; ai >= 0; ai--)
            {
                var a = _asteroids[ai];
                if (WrapDelta(a.Pos - b.Pos).Length() > a.Radius + 2f)
                    continue;

                _bullets.RemoveAt(bi);
                _asteroids.RemoveAt(ai);
                _score += Asteroid.Points[a.Size];
                Explode(a.Pos, a.Vel, 5 + a.Size * 2, a.Radius * 0.7f, false, 0.5f + a.Size * 0.25f);

                if (a.Size > 1)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        Vector2 vel = a.Vel + RandomDir() * (45f + Rand() * 55f);
                        spawned.Add(Asteroid.Create(a.Pos, vel, a.Size - 1, _rng));
                    }
                }
                break;
            }
        }
        _asteroids.AddRange(spawned);

        // Ship vs asteroids
        if (_shipAlive && _invulnerable <= 0)
        {
            foreach (var a in _asteroids)
            {
                if (Rel(a.Pos).Length() < a.Radius + ShipRadius)
                {
                    KillShip();
                    break;
                }
            }
        }
    }

    void KillShip()
    {
        _shipAlive = false;
        _lives--;
        Explode(_shipPos, _shipVel, 12, 22f, true, 1.4f);
        if (_lives <= 0)
            _gameOver = true;
        else
            _respawnTimer = 2f;
    }

    void Explode(Vector2 pos, Vector2 baseVel, int pieces, float length, bool shipColor, float life)
    {
        for (int i = 0; i < pieces; i++)
        {
            float l = life * (0.6f + Rand() * 0.6f);
            _debris.Add(new Debris
            {
                Pos = pos,
                Vel = baseVel * 0.5f + RandomDir() * (30f + Rand() * 110f),
                Angle = Rand() * MathF.Tau,
                Spin = (Rand() - 0.5f) * 8f,
                Length = length * (0.4f + Rand() * 0.8f),
                Life = l,
                MaxLife = l,
                Bright = shipColor,
            });
        }
    }

    // -------------------------------------------------------------------- draw

    public void Draw()
    {
        _center = new Vector2(Gfx.Width / 2f, Gfx.Height / 2f);

        DrawGrid();
        foreach (var a in _asteroids) DrawAsteroid(a);
        foreach (var d in _debris) DrawDebris(d);
        foreach (var b in _bullets) DrawBullet(b);
        if (_shipAlive) DrawShip();
        DrawHud();
    }

    static void Line(Vector2 a, Vector2 b, Color c, float thickness = 1.6f) =>
        Gfx.Line(a, b, thickness, c);

    /// <summary>Faint lattice of "+" markers fixed in the world; the only visual cue for your own motion in empty space.</summary>
    void DrawGrid()
    {
        int n = (int)(World / Grid);
        float extent = ViewExtent + 10f;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Vector2 rel = Rel(new Vector2(i * Grid, j * Grid));
                if (rel.Length() > extent)
                    continue;
                Vector2 p = View(rel);
                // Draw the cross in world axes so it rotates with the world
                Vector2 ax = Rot(new Vector2(5, 0), -_heading);
                Vector2 ay = Rot(new Vector2(0, 5), -_heading);
                Line(p - ax, p + ax, Dim, 1f);
                Line(p - ay, p + ay, Dim, 1f);
            }
        }
    }

    void DrawAsteroid(Asteroid a)
    {
        Vector2 rel = Rel(a.Pos);
        if (rel.Length() > a.Radius + ViewExtent)
            return;

        Vector2 prev = View(rel + Rot(a.Shape[^1], a.Angle));
        foreach (var v in a.Shape)
        {
            Vector2 cur = View(rel + Rot(v, a.Angle));
            Line(prev, cur, Rock);
            prev = cur;
        }
    }

    /// <summary>Distance from the ship to the screen corner: anything further away can't be visible.</summary>
    float ViewExtent => _center.Length() + 10f;

    void DrawBullet(Bullet b)
    {
        Vector2 rel = Rel(b.Pos);
        if (rel.Length() > ViewExtent)
            return;
        Gfx.Circle(View(rel), 2.2f, Amber);
    }

    void DrawDebris(Debris d)
    {
        Vector2 rel = Rel(d.Pos);
        if (rel.Length() > ViewExtent)
            return;
        Vector2 half = Rot(new Vector2(d.Length / 2, 0), d.Angle);
        Color c = Gfx.Fade(d.Bright ? Green : Rock, Math.Clamp(d.Life / d.MaxLife, 0f, 1f));
        Line(View(rel - half), View(rel + half), c);
    }

    /// <summary>The ship never moves or rotates on screen: it is always at the centre, nose up.</summary>
    void DrawShip()
    {
        if (_invulnerable > 0 && ((int)(_time * 12) & 1) == 0)
            return; // blink while invulnerable

        Vector2 c = _center;
        Vector2 nose = c + new Vector2(0, -NoseOffset);
        Vector2 left = c + new Vector2(-12, 14);
        Vector2 notch = c + new Vector2(0, 8);
        Vector2 right = c + new Vector2(12, 14);

        Line(nose, left, Green);
        Line(left, notch, Green);
        Line(notch, right, Green);
        Line(right, nose, Green);

        if (_thrusting)
        {
            float flame = 8f + Rand() * 10f;
            Line(c + new Vector2(-6, 11), c + new Vector2(0, 11 + flame), Amber);
            Line(c + new Vector2(6, 11), c + new Vector2(0, 11 + flame), Amber);
        }
    }

    void DrawHud()
    {
        int w = Gfx.Width;
        int h = Gfx.Height;

        Gfx.Text($"SCORE {_score:D6}", 20, 16, 24, Green);
        Gfx.Text($"WAVE {_wave}", 20, 46, 18, Dim);

        // Lives as little ship outlines
        for (int i = 0; i < _lives; i++)
        {
            Vector2 o = new(30 + i * 28, 90);
            Line(o + new Vector2(0, -10), o + new Vector2(-7, 8), Green, 1.4f);
            Line(o + new Vector2(-7, 8), o + new Vector2(0, 4), Green, 1.4f);
            Line(o + new Vector2(0, 4), o + new Vector2(7, 8), Green, 1.4f);
            Line(o + new Vector2(7, 8), o + new Vector2(0, -10), Green, 1.4f);
        }

        // Compass: shows where world "north" currently is on the rotated screen
        Vector2 cc = new(w - 56, 56);
        Gfx.CircleOutline(cc, 30, Dim);
        Vector2 north = Rot(new Vector2(0, -1), -_heading);
        Line(cc, cc + north * 26, Amber, 2f);
        Vector2 nl = cc + north * 44;
        Gfx.Text("N", (int)nl.X - 5, (int)nl.Y - 8, 16, Amber);

        Gfx.Text($"SPEED {_shipVel.Length():F0}", 20, h - 56, 18, Dim);
        Gfx.Text("A/D or LEFT/RIGHT turn the world   W/UP thrust   S/DOWN reverse   SPACE fire   P pause",
            20, h - 30, 16, Dim);

        if (_paused)
            Centered("PAUSED", h / 2 - 20, 40, Green);

        if (_gameOver)
        {
            Centered("GAME OVER", h / 2 - 50, 56, Green);
            Centered("Press ENTER to play again", h / 2 + 20, 24, Dim);
        }
    }

    static void Centered(string text, int y, int size, Color color)
    {
        int w = Gfx.Measure(text, size);
        Gfx.Text(text, (Gfx.Width - w) / 2, y, size, color);
    }
}


