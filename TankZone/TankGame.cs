using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using WireGames;

namespace TankZone;

/// <summary>
/// A first-person wireframe tank game in the style of the 1980 Atari arcade game Battlezone.
///
/// You sit in a tank on a flat plain dotted with pyramids and blocks, with a mountain range, an
/// erupting volcano and a moon on the horizon. Enemy tanks (later super tanks and guided missiles)
/// close in from all around, so the radar at the top is essential. Flying saucers pay big bonuses.
///
/// The world is a wrapping square. Positions are (x, z) on the ground plane with y up; a heading of 0
/// faces +z and turning right increases it.
/// </summary>
public sealed class TankGame : IGame
{
    // ---- world / camera ---------------------------------------------------------------------------
    const float World = 2400f;       // the plain wraps around; must stay larger than twice DrawRange
    const float DrawRange = 900f;
    const float EyeHeight = 10f;
    const float Near = 6f;

    // ---- player -------------------------------------------------------------------------------------
    const float PlayerSpeed = 85f;
    const float ReverseFactor = 0.6f;
    const float TurnRate = 1.25f;
    const float PlayerRadius = 12f;
    const float PlayerShellSpeed = 700f;
    const float PlayerShellLife = 1.35f;

    // ---- enemies ------------------------------------------------------------------------------------
    const float EnemyShellSpeed = 280f;
    const float EnemyShellLife = 3.4f;
    const int TankPoints = 1000, MissilePoints = 2000, SuperTankPoints = 3000, SaucerPoints = 5000;
    const int MissileThreshold = 5000;   // missiles start to appear
    const int SaucerThreshold = 2000;    // saucers start to appear
    const int LeadForFullAggression = 7000;

    static readonly Color Green = Color.FromArgb(255, 110, 255, 140);
    static readonly Color DimGreen = Color.FromArgb(255, 40, 170, 90);
    static readonly Color Red = Color.FromArgb(255, 255, 80, 70);
    static readonly Color Amber = Color.FromArgb(255, 255, 190, 80);
    static readonly Color White = Color.FromArgb(255, 235, 245, 255);
    static readonly Color Cyan = Color.FromArgb(255, 120, 215, 255);

    enum Phase { Playing, Dead, GameOver }
    enum Kind { Tank, SuperTank, Missile }

    sealed class Obstacle
    {
        public Vector2 Pos;
        public Model Model = null!;
        public float Yaw, Radius;
    }

    sealed class Enemy
    {
        public Kind Kind;
        public Vector2 Pos;
        public float Heading, Age, FireTimer, Seed;
        public float Radius => Kind switch { Kind.Tank => 20f, Kind.SuperTank => 26f, _ => 11f };
    }

    sealed class Saucer
    {
        public Vector2 Pos, Vel;
        public float Phase, TurnTimer;
    }

    sealed class Shell
    {
        public Vector2 Pos, Vel;
        public float Life;
        public bool Mine;
    }

    /// <summary>A piece of debris flying through 3D space.</summary>
    sealed class Bit
    {
        public Vector3 Pos, Vel, Dir;
        public float Len, Life, MaxLife;
        public Color Color;
    }

    /// <summary>A spark thrown out of the volcano (angle offset in radians, height in "sky units").</summary>
    sealed class Lava
    {
        public float Ang, H, VAng, VH;
    }

    readonly Random _rng = new();
    readonly List<Obstacle> _obstacles = new();
    readonly List<Shell> _shells = new();
    readonly List<Bit> _bits = new();
    readonly List<Lava> _lava = new();
    List<List<Vector2>> _crack = new();

    // player
    Vector2 _pos;
    float _heading;
    float _fireCooldown;
    bool _blocked;

    // enemies
    Enemy? _enemy;
    Saucer? _saucer;
    float _spawnTimer, _saucerTimer, _confused, _confusedHeading;
    int _missileLaunches;
    int _enemyScore;

    // game state
    Phase _phase;
    int _score, _high, _lives;
    float _deadTimer, _bonusFlash, _time;
    bool _paused, _grid;

    // per-frame view parameters
    float _w, _h, _band, _hz, _f, _cx, _sinH, _cosH;

    public TankGame()
    {
        BuildObstacles();
        Reset();
    }

    // =========================================================================== setup

    void BuildObstacles()
    {
        // A fixed layout (like the arcade game's ROM table): 21 solids scattered around the start point.
        var r = new Random(1980);
        for (int i = 0; i < 21; i++)
        {
            Model model;
            float radius;
            if (i % 3 == 0) { model = Models.Pyramid; radius = 50f; }
            else if (i % 3 == 1) { model = Models.Cube; radius = 44f; }
            else { model = Models.TallBox; radius = 32f; }

            Vector2 pos;
            int tries = 0;
            do
            {
                pos = new Vector2((float)r.NextDouble() * World, (float)r.NextDouble() * World);
                tries++;
            }
            while (tries < 500 && (Delta(pos).Length() < 170f || _obstacles.Any(o => Delta(pos - o.Pos).Length() < 170f)));

            _obstacles.Add(new Obstacle { Pos = pos, Model = model, Radius = radius, Yaw = (float)r.NextDouble() * MathF.Tau });
        }
    }

    void Reset()
    {
        _score = 0;
        _lives = 3;
        _enemyScore = 0;
        _missileLaunches = 0;
        _enemy = null;
        _saucer = null;
        _spawnTimer = 1.5f;
        _saucerTimer = Rand() * 17f;
        _confused = 0;
        _shells.Clear();
        _bits.Clear();
        _crack.Clear();
        _bonusFlash = 0;
        _paused = false;
        SpawnPlayer();
        _phase = Phase.Playing;
    }

    void SpawnPlayer()
    {
        _pos = Vector2.Zero; // the player always starts at the origin, facing "north"
        _heading = 0f;
        _fireCooldown = 0;
        _blocked = false;
        _shells.Clear();
    }

    // ========================================================================= helpers

    float Rand() => (float)_rng.NextDouble();

    static float Wrap(float v) => v - World * MathF.Floor(v / World);

    static Vector2 WrapPos(Vector2 p) => new(Wrap(p.X), Wrap(p.Y));

    static Vector2 Delta(Vector2 d) =>
        new(d.X - World * MathF.Floor(d.X / World + 0.5f), d.Y - World * MathF.Floor(d.Y / World + 0.5f));

    static Vector2 Fwd(float heading) => new(MathF.Sin(heading), MathF.Cos(heading));

    static float AngleDiff(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    /// <summary>Where a world point is relative to the player (nearest wrapped copy), on the ground plane.</summary>
    Vector2 Rel(Vector2 p) => Delta(p - _pos);

    bool HitsObstacle(Vector2 p, float radius)
    {
        foreach (var o in _obstacles)
        {
            if (Delta(p - o.Pos).Length() < o.Radius + radius)
                return true;
        }
        return false;
    }

    // ========================================================================== update

    public void Update(float dt)
    {
        _time += dt;
        _bonusFlash = MathF.Max(0, _bonusFlash - dt);

        if (Input.Pressed(Keys.G)) _grid = !_grid;
        if (Input.Pressed(Keys.P) && _phase == Phase.Playing) _paused = !_paused;
        if (_paused) return;

        if (_phase == Phase.GameOver && Input.Pressed(Keys.Enter))
        {
            Reset();
            return;
        }

        if (_phase == Phase.Playing)
            UpdatePlayer(dt);

        UpdateEnemy(dt);
        UpdateSaucer(dt);
        UpdateShells(dt);
        UpdateBits(dt);
        UpdateLava(dt);

        if (_phase == Phase.Dead)
        {
            _deadTimer -= dt;
            if (_deadTimer <= 0)
                AfterDeath();
        }
    }

    void UpdatePlayer(float dt)
    {
        float throttle = 0f, turn = 0f;
        if (Input.Down(Keys.Up) || Input.Down(Keys.W)) throttle += 1f;
        if (Input.Down(Keys.Down) || Input.Down(Keys.S)) throttle -= 1f;
        if (Input.Down(Keys.Right) || Input.Down(Keys.D)) turn += 1f;
        if (Input.Down(Keys.Left) || Input.Down(Keys.A)) turn -= 1f;

        _heading = AngleDiff(_heading + turn * TurnRate * dt);

        _blocked = false;
        if (throttle != 0)
        {
            float speed = throttle > 0 ? PlayerSpeed : -PlayerSpeed * ReverseFactor; // reversing is slower
            Vector2 next = WrapPos(_pos + Fwd(_heading) * speed * dt);
            bool tankInWay = _enemy is { Kind: not Kind.Missile } e && Delta(next - e.Pos).Length() < 34f;
            if (HitsObstacle(next, PlayerRadius) || tankInWay)
                _blocked = true;
            else
                _pos = next;
        }

        _fireCooldown = MathF.Max(0, _fireCooldown - dt);
        if (Input.Down(Keys.Space) && _fireCooldown <= 0 && !_shells.Any(s => s.Mine))
        {
            Vector2 f = Fwd(_heading);
            _shells.Add(new Shell { Pos = WrapPos(_pos + f * 12f), Vel = f * PlayerShellSpeed, Life = PlayerShellLife, Mine = true });
            _fireCooldown = 0.25f;
        }
    }

    // ---------------------------------------------------------------------- enemies

    void UpdateEnemy(float dt)
    {
        _confused = MathF.Max(0, _confused - dt);

        if (_enemy == null)
        {
            if (_phase == Phase.Playing)
            {
                _spawnTimer -= dt;
                if (_spawnTimer <= 0)
                    SpawnEnemy();
            }
            return;
        }

        var e = _enemy;
        e.Age += dt;
        e.FireTimer -= dt;

        Vector2 toPlayer = Delta(_pos - e.Pos);
        float dist = toPlayer.Length();
        float bearing = MathF.Atan2(toPlayer.X, toPlayer.Y);
        bool leading = _score - _enemyScore >= LeadForFullAggression;
        bool aggressive = leading || e.Age > 17f;

        if (e.Kind == Kind.Missile)
        {
            UpdateMissile(e, dt, bearing, dist);
            return;
        }

        // Where it wants to head: at you (with a wandering, uncertain wobble until it gets aggressive).
        float desired = bearing;
        if (_confused > 0) desired = _confusedHeading;
        else if (!aggressive) desired += MathF.Sin(e.Age * 0.8f + e.Seed) * 0.9f;

        bool super = e.Kind == Kind.SuperTank;
        float turnRate = (super ? 1.0f : 0.65f) * (aggressive ? 1.3f : 1f);
        float diff = AngleDiff(desired - e.Heading);
        e.Heading = AngleDiff(e.Heading + Math.Clamp(diff, -turnRate * dt, turnRate * dt));

        // Close in to about 200 units, back off if too near, otherwise hold position and turn.
        float want = dist > 240f ? 1f : dist < 140f ? -0.6f : 0f;
        if (want != 0 && MathF.Abs(diff) < 0.9f)
        {
            float speed = super ? 62f : 38f;
            Vector2 next = WrapPos(e.Pos + Fwd(e.Heading) * speed * want * dt);
            if (HitsObstacle(next, 22f) || Delta(next - _pos).Length() < 30f)
                e.Heading = AngleDiff(e.Heading + (e.Seed > 3.14f ? 1f : -1f) * 0.9f * dt); // slide round it
            else
                e.Pos = next;
        }

        // Fire when roughly lined up on you (it can't fire for 2 seconds after appearing).
        float aimTolerance = aggressive ? 0.07f : 0.22f;
        if (_phase == Phase.Playing && e.Age > 2f && e.FireTimer <= 0 && dist < 800f
            && MathF.Abs(AngleDiff(bearing - e.Heading)) < aimTolerance)
        {
            float error = (Rand() - 0.5f) * (aggressive ? 0.06f : 0.24f);
            _shells.Add(new Shell
            {
                Pos = WrapPos(e.Pos + Fwd(e.Heading) * 26f),
                Vel = Fwd(e.Heading + error) * EnemyShellSpeed,
                Life = EnemyShellLife,
            });
            e.FireTimer = (aggressive ? 2.2f + Rand() * 1.3f : 3.5f + Rand() * 2.5f) / (super ? 1.5f : 1f);
        }

        // If you keep evading a slow tank for a minute, it's replaced by a guided missile.
        if (e.Age > 55f + e.Seed * 2f && _score >= MissileThreshold)
        {
            _enemy = null;
            _spawnTimer = 0.5f;
            LaunchMissile();
        }
    }

    void UpdateMissile(Enemy e, float dt, float bearing, float dist)
    {
        float desired = bearing;
        // Later in the game missiles swerve right up until just before impact.
        if (_score >= MissileThreshold + 25000 && dist > 90f)
            desired += MathF.Sin(e.Age * 5f) * 0.55f * MathF.Min(1f, dist / 300f);

        float diff = AngleDiff(desired - e.Heading);
        e.Heading = AngleDiff(e.Heading + Math.Clamp(diff, -1.7f * dt, 1.7f * dt));
        e.Pos = WrapPos(e.Pos + Fwd(e.Heading) * 170f * dt); // missiles hop over obstacles, so no collision test

        if (_phase == Phase.Playing && dist < 17f)
        {
            Explode(e.Pos, 6f, 14, Amber);
            _enemy = null;
            _spawnTimer = 3f;
            KillPlayer();
        }
    }

    void SpawnEnemy()
    {
        if (_score >= MissileThreshold && Rand() < 0.5f)
        {
            LaunchMissile();
            return;
        }

        var kind = _missileLaunches >= 6 ? Kind.SuperTank : Kind.Tank;
        bool leading = _score - _enemyScore >= LeadForFullAggression;
        float dist = DrawRange * (Rand() < 0.5f ? 0.75f : 0.375f);

        Vector2 pos = _pos;
        for (int tries = 0; tries < 30; tries++)
        {
            // While you're not clearly ahead, enemies appear in front of you; once you lead they come from anywhere.
            float angle = leading ? Rand() * MathF.Tau : _heading + (Rand() - 0.5f) * MathF.PI * 0.5f;
            pos = WrapPos(_pos + Fwd(angle) * dist);
            if (!HitsObstacle(pos, 30f))
                break;
        }

        Vector2 toPlayer = Delta(_pos - pos);
        _enemy = new Enemy
        {
            Kind = kind,
            Pos = pos,
            Heading = MathF.Atan2(toPlayer.X, toPlayer.Y),
            Seed = Rand() * MathF.Tau,
            FireTimer = 0,
        };
    }

    void LaunchMissile()
    {
        _missileLaunches++;
        // Missiles always appear at the far distance, within a few degrees of where you're facing.
        float angle = _heading + (Rand() - 0.5f) * 0.28f;
        Vector2 pos = WrapPos(_pos + Fwd(angle) * DrawRange * 0.75f);
        Vector2 toPlayer = Delta(_pos - pos);
        _enemy = new Enemy
        {
            Kind = Kind.Missile,
            Pos = pos,
            Heading = MathF.Atan2(toPlayer.X, toPlayer.Y),
            Seed = Rand() * MathF.Tau,
        };
    }

    void UpdateSaucer(float dt)
    {
        if (_saucer == null)
        {
            if (_score >= SaucerThreshold && _phase == Phase.Playing)
            {
                _saucerTimer -= dt;
                if (_saucerTimer <= 0)
                {
                    float a = Rand() * MathF.Tau;
                    Vector2 vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * (28f + Rand() * 18f);
                    _saucer = new Saucer
                    {
                        Pos = WrapPos(_pos + Fwd(Rand() * MathF.Tau) * (350f + Rand() * 450f)),
                        Vel = vel,
                        TurnTimer = 3f + Rand() * 4f,
                    };
                }
            }
            return;
        }

        // Saucers wander at random (ignoring you and any obstacles) until they're shot down.
        var s = _saucer;
        s.Phase += dt;
        s.Pos = WrapPos(s.Pos + s.Vel * dt);
        s.TurnTimer -= dt;
        if (s.TurnTimer <= 0)
        {
            float a = Rand() * MathF.Tau;
            s.Vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * (28f + Rand() * 18f);
            s.TurnTimer = 3f + Rand() * 4f;
        }
    }

    // ----------------------------------------------------------------- shells and hits

    void UpdateShells(float dt)
    {
        for (int i = _shells.Count - 1; i >= 0; i--)
        {
            var s = _shells[i];
            int steps = Math.Max(1, (int)MathF.Ceiling(s.Vel.Length() * dt / 6f));
            float sub = dt / steps;
            bool dead = false;

            for (int k = 0; k < steps && !dead; k++)
            {
                s.Pos = WrapPos(s.Pos + s.Vel * sub);

                if (HitsObstacle(s.Pos, 1f))
                {
                    Explode(s.Pos, 8f, 4, Amber);
                    dead = true;
                }
                else if (s.Mine)
                {
                    if (_enemy != null && Delta(_enemy.Pos - s.Pos).Length() < _enemy.Radius)
                    {
                        KillEnemy();
                        dead = true;
                    }
                    else if (_saucer != null && Delta(_saucer.Pos - s.Pos).Length() < 26f)
                    {
                        KillSaucer();
                        dead = true;
                    }
                }
                else if (_phase == Phase.Playing && Delta(_pos - s.Pos).Length() < PlayerRadius + 2f)
                {
                    KillPlayer();
                    dead = true;
                }
            }

            s.Life -= dt;
            if (dead || s.Life <= 0)
                _shells.RemoveAt(i);
        }
    }

    void KillEnemy()
    {
        var e = _enemy!;
        Explode(e.Pos, e.Kind == Kind.Missile ? 6f : 8f, e.Kind == Kind.Missile ? 14 : 22, e.Kind == Kind.Missile ? Amber : Green);
        AddScore(e.Kind switch { Kind.Tank => TankPoints, Kind.SuperTank => SuperTankPoints, _ => MissilePoints });
        _enemy = null;
        _spawnTimer = 3f;
    }

    void KillSaucer()
    {
        var s = _saucer!;
        Explode(s.Pos, 40f, 26, Cyan);
        AddScore(SaucerPoints);
        _saucer = null;
        _saucerTimer = Rand() * 17f;
    }

    void AddScore(int points)
    {
        int before = _score;
        _score += points;
        _high = Math.Max(_high, _score);

        // A bonus tank at 15,000 and at 100,000 points.
        if ((before < 15000 && _score >= 15000) || (before < 100000 && _score >= 100000))
        {
            _lives++;
            _bonusFlash = 3f;
        }
    }

    void KillPlayer()
    {
        if (_phase != Phase.Playing)
            return;

        _lives--;
        _enemyScore += 1000; // the enemy gets more confident every time it wins
        _phase = Phase.Dead;
        _deadTimer = 2.8f;
        BuildCrack();
    }

    void AfterDeath()
    {
        if (_lives > 0)
        {
            SpawnPlayer();
            _phase = Phase.Playing;
            _confused = 3f; // the enemy wanders off on a random heading for a few seconds
            _confusedHeading = Rand() * MathF.Tau;
            _crack.Clear();
            if (_enemy is { Kind: Kind.Missile })
            {
                _enemy = null;
                _spawnTimer = 3f;
            }
        }
        else
        {
            _phase = Phase.GameOver;
        }
    }

    // ----------------------------------------------------------------------- debris

    void Explode(Vector2 pos, float y, int count, Color color)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Vector3.Normalize(new Vector3(Rand() - 0.5f, Rand() - 0.3f, Rand() - 0.5f) + new Vector3(0, 0.001f, 0));
            float life = 0.9f + Rand() * 1.1f;
            _bits.Add(new Bit
            {
                Pos = new Vector3(pos.X, y, pos.Y),
                Vel = dir * (40f + Rand() * 110f) + new Vector3(0, 60f + Rand() * 60f, 0),
                Dir = Vector3.Normalize(new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) + new Vector3(0.001f, 0, 0)),
                Len = 5f + Rand() * 12f,
                Life = life,
                MaxLife = life,
                Color = color,
            });
        }
    }

    void UpdateBits(float dt)
    {
        for (int i = _bits.Count - 1; i >= 0; i--)
        {
            var b = _bits[i];
            b.Vel.Y -= 220f * dt;
            b.Pos += b.Vel * dt;
            if (b.Pos.Y < 0)
            {
                b.Pos.Y = 0;
                b.Vel = new Vector3(b.Vel.X * 0.3f, 0, b.Vel.Z * 0.3f);
            }
            b.Life -= dt;
            if (b.Life <= 0)
                _bits.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------------ volcano

    static readonly float VolcanoAngle = 40f * MathF.PI / 180f;
    const float VolcanoHeight = 78f;

    void UpdateLava(float dt)
    {
        bool erupting = _time % 8f < 3.5f;
        int spawn = (int)(dt * (erupting ? 45f : 4f) + Rand());
        for (int i = 0; i < spawn; i++)
        {
            _lava.Add(new Lava
            {
                Ang = (Rand() - 0.5f) * 0.03f,
                H = VolcanoHeight,
                VAng = (Rand() - 0.5f) * 0.07f,
                VH = 60f + Rand() * 130f,
            });
        }

        for (int i = _lava.Count - 1; i >= 0; i--)
        {
            var l = _lava[i];
            l.VH -= 170f * dt;
            l.H += l.VH * dt;
            l.Ang += l.VAng * dt;
            if (l.H < VolcanoHeight - 4f)
                _lava.RemoveAt(i);
        }
    }

    // ============================================================================ draw

    public void Draw()
    {
        _w = Gfx.Width;
        _h = Gfx.Height;
        _band = MathF.Max(120f, _h * 0.2f);
        float playH = _h - _band;
        _hz = _band + playH * 0.55f;
        _f = _w / 2f;       // 90 degree field of view
        _cx = _w / 2f;
        _sinH = MathF.Sin(_heading);
        _cosH = MathF.Cos(_heading);

        // Everything in the 3D view is clipped to below the HUD band.
        Gfx.SetClip(0, _band, _w, playH);
        DrawSky(playH);
        if (_grid) DrawGrid();

        foreach (var o in _obstacles)
            DrawModel(o.Model, o.Pos, 0, o.Yaw, DimGreen);

        if (_enemy is { } e)
        {
            switch (e.Kind)
            {
                case Kind.Tank: DrawModel(Models.Tank, e.Pos, 0, e.Heading, Green); break;
                case Kind.SuperTank: DrawModel(Models.SuperTank, e.Pos, 0, e.Heading, Red); break;
                default:
                    float hop = HitsObstacle(e.Pos, 20f) ? 36f : 0f; // missiles skip over obstacles
                    DrawModel(Models.Missile, e.Pos, hop, e.Heading, Amber);
                    break;
            }
        }

        if (_saucer is { } sa)
            DrawModel(Models.Saucer, sa.Pos, 42f + MathF.Sin(sa.Phase * 2f) * 5f, sa.Phase * 1.6f, Cyan);

        foreach (var s in _shells) DrawShell(s);
        foreach (var b in _bits) DrawBit(b);

        DrawSight();
        Gfx.ResetClip();

        DrawHud();
        if (_phase != Phase.Playing)
            DrawCrack();
        DrawMessages();
    }

    static Color Fog(Color c, float dist) => Gfx.Fade(c, MathF.Max(0.2f, 1f - dist / DrawRange * 0.8f));

    /// <summary>Camera space: x right, y up (relative to the eye), z straight ahead.</summary>
    Vector3 ToCam(Vector2 rel, float y) =>
        new(rel.X * _cosH - rel.Y * _sinH, y - EyeHeight, rel.X * _sinH + rel.Y * _cosH);

    Vector2 Project(Vector3 p) => new(_cx + _f * p.X / p.Z, _hz - _f * p.Y / p.Z);

    void Line3(Vector3 a, Vector3 b, Color c, float width = 1.5f)
    {
        // Clip against the near plane so lines passing behind the camera don't wrap around the screen.
        if (a.Z < Near && b.Z < Near)
            return;
        if (a.Z < Near)
            a += (b - a) * ((Near - a.Z) / (b.Z - a.Z));
        else if (b.Z < Near)
            b += (a - b) * ((Near - b.Z) / (a.Z - b.Z));
        Gfx.Line(Project(a), Project(b), width, c);
    }

    void DrawModel(Model m, Vector2 pos, float y0, float yaw, Color color)
    {
        Vector2 rel = Rel(pos);
        float dist = rel.Length();
        if (dist > DrawRange)
            return;
        if (rel.X * _sinH + rel.Y * _cosH < -m.Radius - 10f)
            return; // wholly behind us

        float sy = MathF.Sin(yaw), cy = MathF.Cos(yaw);
        Color c = Fog(color, dist);
        Vector3 Cam(Vector3 p) => ToCam(rel + new Vector2(p.X * cy + p.Z * sy, -p.X * sy + p.Z * cy), y0 + p.Y);
        foreach (var (a, b) in m.Edges)
            Line3(Cam(a), Cam(b), c);
    }

    void DrawShell(Shell s)
    {
        Vector2 rel = Rel(s.Pos);
        Vector2 back = Vector2.Normalize(s.Vel) * 9f;
        Color c = s.Mine ? White : Amber;
        Line3(ToCam(rel, 8f), ToCam(rel - back, 8f), c, 2.6f);
    }

    void DrawBit(Bit b)
    {
        Vector2 rel = Rel(new Vector2(b.Pos.X, b.Pos.Z));
        Vector3 half = b.Dir * (b.Len / 2f);
        Color c = Gfx.Fade(b.Color, b.Life / b.MaxLife);
        Line3(ToCam(rel + new Vector2(half.X, half.Z), b.Pos.Y + half.Y),
              ToCam(rel - new Vector2(half.X, half.Z), b.Pos.Y - half.Y), c, 1.4f);
    }

    /// <summary>A flat lattice on the ground (toggle with G): not in the arcade original, but a useful cue for movement.</summary>
    void DrawGrid()
    {
        const float step = 100f;
        float bx = MathF.Floor(_pos.X / step) * step;
        float bz = MathF.Floor(_pos.Y / step) * step;
        Color c = Gfx.Fade(DimGreen, 0.35f);
        for (int k = -9; k <= 10; k++)
        {
            float rx = bx + k * step - _pos.X;
            Line3(ToCam(new Vector2(rx, -DrawRange), 0), ToCam(new Vector2(rx, DrawRange), 0), c, 1f);
            float rz = bz + k * step - _pos.Y;
            Line3(ToCam(new Vector2(-DrawRange, rz), 0), ToCam(new Vector2(DrawRange, rz), 0), c, 1f);
        }
    }

    // ---------------------------------------------------------------------------- sky

    static readonly float[] Profile = BuildProfile();

    static float[] BuildProfile()
    {
        var r = new Random(1980);
        var raw = new float[360];
        for (int i = 0; i < 360; i++)
            raw[i] = (float)r.NextDouble();

        var p = new float[360];
        for (int i = 0; i < 360; i++)
        {
            float noise = (raw[(i + 359) % 360] + raw[i] * 2f + raw[(i + 1) % 360]) / 4f;
            float swell = 0.5f + 0.5f * MathF.Sin(i * MathF.PI / 180f * 3f + 1f);
            p[i] = 0.6f * noise + 0.4f * swell;

            // keep the range low around the volcano
            float dDeg = MathF.Abs(((i - 40 + 540) % 360) - 180);
            if (dDeg < 9) p[i] *= 0.3f;
        }
        return p;
    }

    static float MountainHeight(float angle)
    {
        float deg = angle * 180f / MathF.PI;
        deg -= 360f * MathF.Floor(deg / 360f);
        int i = (int)deg % 360;
        float t = deg - MathF.Floor(deg);
        return Profile[i] + (Profile[(i + 1) % 360] - Profile[i]) * t;
    }

    float SkyX(float angleOffset) => _cx + _f * MathF.Tan(angleOffset);

    void DrawSky(float playH)
    {
        float k = playH / 560f; // scale sky features with the window height

        // horizon and mountain range
        Gfx.Line(new Vector2(0, _hz), new Vector2(_w, _hz), 1f, Gfx.Fade(Green, 0.5f));
        Vector2? prev = null;
        for (float sx = 0; sx <= _w + 6; sx += 5f)
        {
            float ang = _heading + MathF.Atan((sx - _cx) / _f);
            var p = new Vector2(sx, _hz - (8f + MountainHeight(ang) * 46f) * k);
            if (prev is { } q)
                Gfx.Line(q, p, 1.4f, Green);
            prev = p;
        }

        // volcano
        float da = AngleDiff(VolcanoAngle - _heading);
        if (MathF.Abs(da) < 1.1f)
        {
            float hv = VolcanoHeight * k;
            Vector2[] v =
            {
                new(SkyX(da - 0.09f), _hz), new(SkyX(da - 0.022f), _hz - hv), new(SkyX(da + 0.022f), _hz - hv), new(SkyX(da + 0.09f), _hz),
            };
            for (int i = 0; i < 3; i++)
                Gfx.Line(v[i], v[i + 1], 1.5f, Green);

            foreach (var l in _lava)
            {
                float x = SkyX(AngleDiff(VolcanoAngle + l.Ang - _heading));
                float y = _hz - l.H * k;
                Gfx.Line(new Vector2(x, y), new Vector2(x - l.VAng * 120f * k, y + l.VH * 0.04f * k), 1.6f, Red);
            }
        }

        // crescent moon
        float md = AngleDiff(-0.9f - _heading);
        if (MathF.Abs(md) < 1.1f)
        {
            Vector2 c = new(SkyX(md), _hz - 175f * k);
            float rad = 24f * k;
            Vector2 prevOuter = default, prevInner = default;
            for (int i = 0; i <= 14; i++)
            {
                float t = MathF.PI / 2f + i * MathF.PI / 14f;
                Vector2 outer = c + new Vector2(MathF.Cos(t), -MathF.Sin(t)) * rad;
                Vector2 inner = c + new Vector2(MathF.Cos(t) * rad * 0.5f, -MathF.Sin(t) * rad);
                if (i > 0)
                {
                    Gfx.Line(prevOuter, outer, 1.3f, White);
                    Gfx.Line(prevInner, inner, 1.3f, White);
                }
                prevOuter = outer;
                prevInner = inner;
            }
        }
    }

    /// <summary>The gun sight: four ticks around the aiming point on the horizon.</summary>
    void DrawSight()
    {
        float y = _hz;
        Color c = Gfx.Fade(White, 0.85f);
        Gfx.Line(new Vector2(_cx - 30, y), new Vector2(_cx - 10, y), 1.6f, c);
        Gfx.Line(new Vector2(_cx + 10, y), new Vector2(_cx + 30, y), 1.6f, c);
        Gfx.Line(new Vector2(_cx, y - 30), new Vector2(_cx, y - 10), 1.6f, c);
        Gfx.Line(new Vector2(_cx, y + 10), new Vector2(_cx, y + 30), 1.6f, c);
    }

    // ---------------------------------------------------------------------------- HUD

    void DrawHud()
    {
        int w = (int)_w, band = (int)_band;
        Gfx.Line(new Vector2(0, band), new Vector2(w, band), 2f, Red);

        Gfx.Text($"SCORE  {_score:D6}", 20, 14, 24, Red);
        Gfx.Text($"HIGH   {_high:D6}", 20, 46, 18, Gfx.Fade(Red, 0.75f));

        // spare tanks
        for (int i = 0; i < _lives; i++)
            DrawTankIcon(new Vector2(w - 40 - i * 44, 30), Red);

        DrawRadar(new Vector2(w / 2f, band / 2f), band * 0.42f);

        string? msg = StatusMessage();
        if (msg != null && (_time * 3f % 1f) < 0.7f)
            Gfx.Text(msg, 20, band - 34, 18, Red);

        if (_saucer != null && _phase == Phase.Playing && (_time * 4f % 1f) < 0.6f)
            Gfx.Text("FLYING SAUCER!", w - 220, band - 34, 18, Cyan);
    }

    string? StatusMessage()
    {
        if (_phase != Phase.Playing)
            return null;
        if (_blocked)
            return "MOTION BLOCKED BY OBJECT";
        if (_enemy == null)
            return null;

        Vector2 rel = Rel(_enemy.Pos);
        float dist = rel.Length();
        Vector3 cam = ToCam(rel, EyeHeight);
        float ang = MathF.Atan2(cam.X, cam.Z); // 0 = dead ahead, positive = to the right
        if (MathF.Abs(ang) < 0.6f && dist < 650f) return "ENEMY IN RANGE";
        if (MathF.Abs(ang) > 2.2f) return "ENEMY TO REAR";
        if (MathF.Abs(ang) > 0.8f) return ang < 0 ? "ENEMY TO LEFT" : "ENEMY TO RIGHT";
        return null;
    }

    void DrawRadar(Vector2 c, float r)
    {
        const float range = DrawRange * 1.15f;
        Gfx.CircleOutline(c, r, Gfx.Fade(Red, 0.9f));
        Gfx.CircleOutline(c, r * 0.5f, Gfx.Fade(Red, 0.35f));

        // field-of-view lines (the 90 degree window you actually see)
        float s45 = MathF.Sin(MathF.PI / 4f) * r;
        Gfx.Line(c, c + new Vector2(-s45, -s45), 1f, Gfx.Fade(Red, 0.5f));
        Gfx.Line(c, c + new Vector2(s45, -s45), 1f, Gfx.Fade(Red, 0.5f));

        // sweep
        float sweep = _time * 1.7f % MathF.Tau;
        Gfx.Line(c, c + new Vector2(MathF.Sin(sweep), -MathF.Cos(sweep)) * r, 1f, Gfx.Fade(Red, 0.55f));

        // blips: enemy tanks and missiles (never saucers or obstacles); they flash as the sweep passes
        if (_enemy != null && _phase != Phase.GameOver)
        {
            Vector3 cam = ToCam(Rel(_enemy.Pos), EyeHeight);
            Vector2 p = new Vector2(cam.X, -cam.Z) * (r / range);
            if (p.Length() > r - 3f)
                p = Vector2.Normalize(p) * (r - 3f);

            float blipAngle = MathF.Atan2(cam.X, cam.Z);
            if (blipAngle < 0) blipAngle += MathF.Tau;
            float since = sweep - blipAngle;
            if (since < 0) since += MathF.Tau;
            float bright = MathF.Max(0.25f, 1f - since / MathF.Tau);

            Color col = Gfx.Fade(_enemy.Kind == Kind.Missile ? Amber : Red, bright);
            float size = _enemy.Kind == Kind.Missile ? 3f : 4.5f;
            Gfx.FillRect(c.X + p.X - size / 2f, c.Y + p.Y - size / 2f, size, size, col);
        }

        // you
        Gfx.Line(c + new Vector2(0, -5), c + new Vector2(-3.5f, 3), 1.4f, Red);
        Gfx.Line(c + new Vector2(-3.5f, 3), c + new Vector2(3.5f, 3), 1.4f, Red);
        Gfx.Line(c + new Vector2(3.5f, 3), c + new Vector2(0, -5), 1.4f, Red);
    }

    static void DrawTankIcon(Vector2 p, Color c)
    {
        Gfx.Line(p + new Vector2(-14, 4), p + new Vector2(14, 4), 1.4f, c);
        Gfx.Line(p + new Vector2(14, 4), p + new Vector2(10, -3), 1.4f, c);
        Gfx.Line(p + new Vector2(10, -3), p + new Vector2(-10, -3), 1.4f, c);
        Gfx.Line(p + new Vector2(-10, -3), p + new Vector2(-14, 4), 1.4f, c);
        Gfx.Line(p + new Vector2(-5, -3), p + new Vector2(-5, -8), 1.4f, c);
        Gfx.Line(p + new Vector2(-5, -8), p + new Vector2(5, -8), 1.4f, c);
        Gfx.Line(p + new Vector2(5, -8), p + new Vector2(5, -3), 1.4f, c);
        Gfx.Line(p + new Vector2(5, -6), p + new Vector2(16, -6), 1.4f, c);
    }

    // ----------------------------------------------------------- windshield and messages

    /// <summary>When you're hit the windshield cracks. The pattern is built once per death, in unit coordinates.</summary>
    void BuildCrack()
    {
        _crack = new List<List<Vector2>>();
        Vector2 origin = new(0.35f + Rand() * 0.3f, 0.4f + Rand() * 0.25f);
        int rays = 10 + _rng.Next(4);
        for (int i = 0; i < rays; i++)
        {
            float a = i * MathF.Tau / rays + (Rand() - 0.5f) * 0.35f;
            var pts = new List<Vector2> { origin };
            Vector2 p = origin;
            float len = 0.45f + Rand() * 0.5f;
            int segs = 7;
            for (int k = 1; k <= segs; k++)
            {
                float jitter = (Rand() - 0.5f) * 0.55f;
                p += new Vector2(MathF.Cos(a + jitter), MathF.Sin(a + jitter)) * (len / segs);
                pts.Add(p);
            }
            _crack.Add(pts);
        }
    }

    void DrawCrack()
    {
        if (_crack.Count == 0)
            return;

        float alpha = _phase == Phase.Dead ? Math.Clamp((2.8f - _deadTimer) * 6f, 0f, 1f) : 1f;
        Color c = Gfx.Fade(White, 0.9f * alpha);
        Vector2 S(Vector2 u) => new(u.X * _w, u.Y * _h);

        foreach (var ray in _crack)
        {
            for (int i = 0; i < ray.Count - 1; i++)
                Gfx.Line(S(ray[i]), S(ray[i + 1]), 1.3f, c);
        }

        // concentric chords between neighbouring cracks
        foreach (int ring in new[] { 2, 4, 6 })
        {
            for (int i = 0; i < _crack.Count; i++)
            {
                var a = _crack[i];
                var b = _crack[(i + 1) % _crack.Count];
                if (ring < a.Count && ring < b.Count)
                    Gfx.Line(S(a[ring]), S(b[ring]), 1f, Gfx.Fade(White, 0.55f * alpha));
            }
        }
    }

    void DrawMessages()
    {
        int h = (int)_h;

        if (_bonusFlash > 0 && (_time * 5f % 1f) < 0.65f)
            Centered("BONUS TANK!", (int)_band + 30, 30, Amber);

        if (_paused)
            Centered("PAUSED", h / 2 - 20, 40, Green);

        if (_phase == Phase.GameOver)
        {
            Centered("GAME OVER", h / 2 - 50, 56, Red);
            Centered("Press ENTER to play again", h / 2 + 20, 24, Amber);
        }

        Gfx.Text("W/S drive   A/D turn   SPACE fire   G ground grid   P pause   ESC menu", 20, h - 26, 16, DimGreen);
    }

    static void Centered(string text, int y, int size, Color color) =>
        Gfx.Text(text, (Gfx.Width - Gfx.Measure(text, size)) / 2, y, size, color);
}
