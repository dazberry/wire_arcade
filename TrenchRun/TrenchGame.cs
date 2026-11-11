using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using WireGames;

namespace TrenchRun;

/// <summary>
/// A first-person wireframe space-assault game in the style of Atari's 1983 vector arcade game "Star Wars".
///
/// Each wave has three stages:
///   1. SPACE   - a dogfight with enemy interceptors that spit fireballs at you
///   2. SURFACE - skim the surface of the battle station past bunkers and (from wave 2) towers
///   3. TRENCH  - fly the trench past turrets and catwalks, fend off a dark fighter, then put a single
///                proton torpedo into the reactor port at the end.
///
/// Arrow keys / WASD move the targeting cross-hair; in the surface and trench stages your ship follows it,
/// so aiming and steering are one control. Space fires.
///
/// World axes: x right, y up, z forward (the direction of flight). The camera never rotates.
/// </summary>
public sealed class TrenchGame : IGame
{
    // ---- view ---------------------------------------------------------------------------------------
    const float Near = 8f;
    const float DrawFar = 3200f;

    // ---- trench geometry ------------------------------------------------------------------------------
    const float TW = 230f;   // half-width of the trench
    const float TH = 280f;   // depth of the trench
    const float PortRange = 3200f; // the reactor port comes into view this far from the end

    // ---- player ---------------------------------------------------------------------------------------
    const int MaxShields = 6;
    const float AimSpeed = 560f;       // cross-hair speed in pixels/s
    const float SurfaceSpeed = 900f;
    const float TrenchSpeed = 1000f;
    const float LaserSpeed = 2800f;
    const float TorpedoSpeed = 2200f;
    const float FireDelay = 0.14f;
    const float ShipHitX = 36f, ShipHitY = 30f;

    // ---- scoring --------------------------------------------------------------------------------------
    const int FireballPoints = 33, TurretPoints = 100, BunkerPoints = 200, InterceptorPoints = 1000;
    const int DarkHitPoints = 2000, PortPoints = 25000, AllTowersBonus = 50000, ShieldBonus = 5000;
    static readonly int[] ForceBonus = { 5000, 10000, 25000, 50000, 100000 };

    static readonly Color Cyan = Color.FromArgb(255, 120, 215, 255);
    static readonly Color Green = Color.FromArgb(255, 110, 255, 140);
    static readonly Color Red = Color.FromArgb(255, 255, 80, 70);
    static readonly Color Amber = Color.FromArgb(255, 255, 190, 80);
    static readonly Color White = Color.FromArgb(255, 235, 245, 255);
    static readonly Color Blue = Color.FromArgb(255, 90, 130, 255);
    static readonly Color Dim = Color.FromArgb(255, 60, 90, 130);

    enum Stage { Space, Surface, Trench, Victory, GameOver }
    enum EKind { Interceptor, Dark, Fireball, Bunker, TowerTop, Turret }

    /// <summary>Anything that can be shot or that shoots: enemies, fireballs and fixed gun emplacements.</summary>
    sealed class Ent
    {
        public EKind Kind;
        public Vector3 Pos, Vel, Aim;
        public float Radius, FireTimer, Spin, Age, Sway;
        public int Hits;
        public bool Leaving;
        public Tower? Tower;
    }

    sealed class Bolt
    {
        public Vector3 Pos, Dir;
        public float Life;
        public bool Torpedo;
    }

    sealed class Tower
    {
        public float X, Z, Height;
        public Model Pole = null!;
        public bool Hit, TopAlive = true;
    }

    sealed class Catwalk
    {
        public float Z, Y;
        public bool Hit;
    }

    sealed class Deco
    {
        public Vector3 Pos;
        public Model Model = null!;
    }

    sealed class Trigger
    {
        public float Z;
        public int Type; // 0 = a pair of interceptors, 1 = the dark fighter
        public bool Done;
    }

    sealed class Bit
    {
        public Vector3 Pos, Vel, Dir;
        public float Len, Life, MaxLife;
        public Color Color;
    }

    readonly Random _rng = new();
    readonly List<Ent> _ents = new();
    readonly List<Bolt> _bolts = new();
    readonly List<Bit> _bits = new();
    readonly List<Tower> _towers = new();
    readonly List<Catwalk> _catwalks = new();
    readonly List<Deco> _decos = new();
    readonly List<Trigger> _triggers = new();
    readonly List<string> _victoryLines = new();
    readonly Vector3[] _stars;

    Stage _stage;
    int _wave, _score, _high, _shields;
    float _time, _stageTime, _flash, _invuln, _fireCooldown;
    bool _paused, _gunLeft;
    string _banner = "", _subBanner = "";
    float _bannerTimer;

    // camera / aiming
    Vector3 _cam, _camPrev, _shake;
    Vector2 _aim;

    // space
    int _kills, _quota;
    float _spawnTimer, _endDelay;

    // surface
    float _surfaceLen;
    int _topsTotal, _topsKilled;

    // trench
    float _trenchLen;
    bool _portActive, _torpedoFired;
    int _trenchShots;
    float _victoryTimer;

    // per-frame view parameters
    float _w = 1280, _h = 720, _f = 760, _cx = 640, _cy = 360;
    Vector3 _view;

    public TrenchGame()
    {
        var r = new Random(1983);
        _stars = new Vector3[160];
        for (int i = 0; i < _stars.Length; i++)
            _stars[i] = new Vector3((float)r.NextDouble(), (float)r.NextDouble(), 0.25f + (float)r.NextDouble() * 0.75f);
        NewGame();
    }

    // =========================================================================== setup

    float Rand() => (float)_rng.NextDouble();

    float Range(float a, float b) => a + Rand() * (b - a);

    void NewGame()
    {
        _wave = 1;
        _score = 0;
        _shields = MaxShields;
        _flash = _invuln = 0;
        _aim = Vector2.Zero;
        _paused = false;
        _bits.Clear();
        StartSpace();
    }

    void Banner(string text, string sub = "", float seconds = 2.6f)
    {
        _banner = text;
        _subBanner = sub;
        _bannerTimer = seconds;
    }

    // wave difficulty
    float EnemySpeed => 440f + 30f * MathF.Min(_wave, 10);
    float FireballSpeed => 330f + 22f * MathF.Min(_wave, 10);
    float FireScale => 1f / (1f + 0.12f * MathF.Min(_wave, 10)); // shorter delays on later waves
    int SpaceQuota => Math.Min(5 + _wave, 12);
    float SurfaceLength => MathF.Min(9500f + 1000f * _wave, 17000f);
    float TrenchLength => MathF.Min(12000f + 1500f * _wave, 24000f);
    int DarkHitsNeeded => Math.Min(3 + 2 * _wave, 25);

    // ---------------------------------------------------------------- space stage

    void StartSpace()
    {
        _stage = Stage.Space;
        _stageTime = 0;
        _kills = 0;
        _quota = SpaceQuota;
        _spawnTimer = 1.2f;
        _endDelay = 0;
        _ents.Clear();
        _bolts.Clear();
        _cam = _camPrev = Vector3.Zero;
        Banner($"WAVE {_wave}", "enemy interceptors approaching");
    }

    void SpawnInterceptor()
    {
        Vector3 pos = new(Range(-900f, 900f), Range(-450f, 450f), _cam.Z + Range(2300f, 2800f));
        Vector3 aim = new(Range(-130f, 130f), Range(-90f, 90f), _cam.Z - 60f);
        _ents.Add(new Ent
        {
            Kind = EKind.Interceptor,
            Pos = pos,
            Aim = aim,
            Vel = Vector3.Normalize(aim - pos) * EnemySpeed,
            Radius = 30f,
            FireTimer = Range(0.8f, 2.2f),
            Sway = Rand() * 6f,
        });
    }

    // --------------------------------------------------------------- surface stage

    void StartSurface()
    {
        _stage = Stage.Surface;
        _stageTime = 0;
        _surfaceLen = SurfaceLength;
        _cam = _camPrev = new Vector3(0, 150f, 0);
        _ents.Clear();
        _bolts.Clear();
        _towers.Clear();
        _decos.Clear();
        _topsTotal = _topsKilled = 0;

        // laser bunkers
        int bunkers = Math.Min(8 + 2 * _wave, 20);
        for (int i = 0; i < bunkers; i++)
        {
            float z = 1400f + (i + Rand()) * (_surfaceLen - 2200f) / bunkers;
            _ents.Add(new Ent
            {
                Kind = EKind.Bunker,
                Pos = new Vector3(Range(-520f, 520f), 12f, z),
                Radius = 30f,
                FireTimer = Range(0.5f, 2.5f),
            });
        }

        // towers (from wave 2): hitting one costs a shield, shooting the top scores
        if (_wave >= 2)
        {
            int towers = Math.Min(6 + 2 * _wave, 18);
            for (int i = 0; i < towers; i++)
            {
                float z = 1700f + (i + Rand() * 0.8f) * (_surfaceLen - 2600f) / towers;
                float height = Range(90f, 260f);
                var t = new Tower { X = Range(-460f, 460f), Z = z, Height = height, Pole = Models.TowerPole(height) };
                _towers.Add(t);
                _ents.Add(new Ent
                {
                    Kind = EKind.TowerTop,
                    Pos = new Vector3(t.X, height + 9f, z),
                    Radius = 24f,
                    Tower = t,
                });
                _topsTotal++;
            }
        }

        // scenery: low blocks on the surface so you can see how fast you're going
        for (int i = 0; i < 90; i++)
        {
            float w = Range(10f, 38f), d = Range(10f, 38f), hgt = Range(4f, 16f);
            _decos.Add(new Deco
            {
                Pos = new Vector3(Range(-1000f, 1000f), 0, Range(0, _surfaceLen + DrawFar)),
                Model = new Model().Box(0, 0, w, d, 0, hgt),
            });
        }

        Banner("SURFACE RUN", _topsTotal > 0 ? "destroy the tower tops - avoid the towers" : "destroy the laser bunkers");
    }

    void EndSurface()
    {
        if (_topsTotal > 0 && _topsKilled == _topsTotal)
        {
            _score += AllTowersBonus;
            _high = Math.Max(_high, _score);
        }
        StartTrench();
    }

    // ---------------------------------------------------------------- trench stage

    void StartTrench()
    {
        _stage = Stage.Trench;
        _stageTime = 0;
        _trenchLen = TrenchLength;
        _cam = _camPrev = new Vector3(Math.Clamp(_cam.X, -TW + 40f, TW - 40f), TH - 36f, 0);
        _ents.Clear();
        _bolts.Clear();
        _catwalks.Clear();
        _triggers.Clear();
        _portActive = _torpedoFired = false;
        _trenchShots = 0;

        // wall and floor turrets
        for (float z = 1300f; z < _trenchLen - PortRange - 300f; z += Range(220f, 420f))
        {
            int side = _rng.Next(3) - 1; // -1 left wall, +1 right wall, 0 floor
            Vector3 pos = side == 0
                ? new Vector3(Range(-TW * 0.6f, TW * 0.6f), 8f, z)
                : new Vector3(side * (TW - 8f), Range(40f, TH - 50f), z);
            _ents.Add(new Ent { Kind = EKind.Turret, Pos = pos, Radius = 20f, FireTimer = Range(0.5f, 3f) });
        }

        // catwalks (from wave 2): fly over or under them
        if (_wave >= 2)
        {
            for (float z = 3200f; z < _trenchLen - PortRange - 600f; z += Range(2000f, 3000f))
                _catwalks.Add(new Catwalk { Z = z, Y = Range(80f, TH - 90f) });
        }

        _triggers.Add(new Trigger { Z = 0.16f * _trenchLen, Type = 0 });
        _triggers.Add(new Trigger { Z = 0.34f * _trenchLen, Type = 0 });
        _triggers.Add(new Trigger { Z = 0.50f * _trenchLen, Type = 1 });
        _triggers.Add(new Trigger { Z = 0.68f * _trenchLen, Type = 0 });

        Banner("TRENCH RUN", "use the force");
    }

    void FireTorpedo()
    {
        Vector3 from = _cam + new Vector3(0, -16f, 8f);
        Vector3 target = _cam + AimDir() * 3000f;
        _bolts.Add(new Bolt { Pos = from, Dir = Vector3.Normalize(target - from), Life = 4f, Torpedo = true });
        _torpedoFired = true;
    }

    void PortDestroyed()
    {
        _stage = Stage.Victory;
        _victoryTimer = 0;
        _victoryLines.Clear();
        _score += PortPoints;
        _victoryLines.Add($"REACTOR DESTROYED   +{PortPoints}");

        if (_trenchShots == 0)
        {
            int bonus = ForceBonus[Math.Min(_wave - 1, ForceBonus.Length - 1)];
            _score += bonus;
            _victoryLines.Add($"USE THE FORCE BONUS   +{bonus}");
        }

        int shieldBonus = ShieldBonus * _shields;
        _score += shieldBonus;
        _victoryLines.Add($"SHIELD BONUS   +{shieldBonus}");
        _high = Math.Max(_high, _score);

        _flash = 1f;
        Explode(new Vector3(0, 14f, _trenchLen), 90, White, 420f);
        Explode(new Vector3(0, 14f, _trenchLen), 60, Amber, 300f);
    }

    /// <summary>The run ended without destroying the port: lose a shield and go round again.</summary>
    void MissedRun()
    {
        HitPlayer(force: true);
        if (_stage == Stage.GameOver)
            return;

        _cam = _camPrev = new Vector3(_cam.X, _cam.Y, _trenchLen - PortRange - 2400f);
        _ents.RemoveAll(e => e.Kind == EKind.Fireball || e.Kind == EKind.Interceptor);
        _bolts.Clear();
        _portActive = _torpedoFired = false;
        _triggers.Add(new Trigger { Z = _cam.Z + 900f, Type = 0 });
        Banner("TRY AGAIN", "line up on the reactor port");
    }

    // ========================================================================= helpers

    Vector3 AimDir() => Vector3.Normalize(new Vector3(_aim.X / _f, -_aim.Y / _f, 1f));

    static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

    void HitPlayer(bool force = false)
    {
        if (_invuln > 0 && !force)
            return;
        _shields--;
        _flash = 1f;
        _invuln = 0.9f;
        Explode(_cam + new Vector3(0, 0, 60f), 10, Red, 160f);
        if (_shields <= 0)
        {
            _stage = Stage.GameOver;
            _high = Math.Max(_high, _score);
        }
    }

    void Explode(Vector3 pos, int count, Color color, float speed = 150f)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Vector3.Normalize(new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) + new Vector3(0.001f, 0, 0));
            float life = Range(0.7f, 1.6f);
            _bits.Add(new Bit
            {
                Pos = pos,
                Vel = dir * Range(speed * 0.3f, speed),
                Dir = Vector3.Normalize(new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) + new Vector3(0.001f, 0, 0)),
                Len = Range(6f, 20f),
                Life = life,
                MaxLife = life,
                Color = color,
            });
        }
    }

    void SpawnFireball(Vector3 from)
    {
        // Aimed at where you are now, allowing for how far you'll have flown forward before it arrives.
        float forward = _stage == Stage.Surface ? SurfaceSpeed : _stage == Stage.Trench ? TrenchSpeed : 0f;
        float dz = MathF.Max(from.Z - _cam.Z, 50f);
        float t = dz / (FireballSpeed + forward);
        Vector3 target = new(_cam.X + Range(-12f, 12f), _cam.Y + Range(-10f, 10f), _cam.Z + forward * t);
        Vector3 dir = Vector3.Normalize(target - from);
        _ents.Add(new Ent { Kind = EKind.Fireball, Pos = from, Vel = dir * FireballSpeed, Radius = 16f });
    }

    // ========================================================================== update

    public void Update(float dt)
    {
        _time += dt;
        if (Input.Pressed(Keys.P) && _stage != Stage.GameOver)
            _paused = !_paused;
        if (_paused)
            return;

        if (_stage == Stage.GameOver && Input.Pressed(Keys.Enter))
        {
            NewGame();
            return;
        }

        _flash = MathF.Max(0, _flash - dt * 1.2f);
        _invuln = MathF.Max(0, _invuln - dt);
        _bannerTimer -= dt;
        _fireCooldown = MathF.Max(0, _fireCooldown - dt);
        _shake = _flash > 0.4f ? new Vector3(Range(-1f, 1f), Range(-1f, 1f), 0) * 7f * _flash : Vector3.Zero;

        if (_stage != Stage.GameOver && _stage != Stage.Victory)
        {
            UpdateAim(dt);
            HandleFire();
        }

        _camPrev = _cam;
        switch (_stage)
        {
            case Stage.Space: UpdateSpace(dt); break;
            case Stage.Surface: UpdateSurface(dt); break;
            case Stage.Trench: UpdateTrench(dt); break;
            case Stage.Victory: UpdateVictory(dt); break;
        }

        UpdateEntities(dt);
        UpdateBolts(dt);
        UpdateBits(dt);
    }

    void UpdateAim(float dt)
    {
        Vector2 v = Vector2.Zero;
        if (Input.Down(Keys.Left) || Input.Down(Keys.A)) v.X -= 1;
        if (Input.Down(Keys.Right) || Input.Down(Keys.D)) v.X += 1;
        if (Input.Down(Keys.Up) || Input.Down(Keys.W)) v.Y -= 1;
        if (Input.Down(Keys.Down) || Input.Down(Keys.S)) v.Y += 1;
        _aim += v * AimSpeed * dt;
        _aim = new Vector2(Math.Clamp(_aim.X, -_w * 0.40f, _w * 0.40f), Math.Clamp(_aim.Y, -_h * 0.36f, _h * 0.36f));
    }

    void HandleFire()
    {
        if (_portActive)
        {
            // Close to the reactor the guns switch to a single proton torpedo.
            if (Input.Pressed(Keys.Space) && !_torpedoFired)
                FireTorpedo();
            return;
        }

        if (Input.Down(Keys.Space) && _fireCooldown <= 0)
        {
            _gunLeft = !_gunLeft;
            Vector3 gun = _cam + new Vector3(_gunLeft ? -34f : 34f, -20f, 6f);

            // The guns converge on whatever the cross-hair is on (or far ahead if it's on nothing),
            // so a shot lands where you point it no matter how far away the target is.
            Vector3 dir = AimDir();
            float converge = 1800f;
            foreach (var e in _ents)
            {
                Vector3 rel = e.Pos - _cam;
                float along = Vector3.Dot(rel, dir);
                if (along > 100f && along < converge && (rel - dir * along).Length() < e.Radius * 1.5f)
                    converge = along;
            }
            Vector3 target = _cam + dir * converge;
            _bolts.Add(new Bolt { Pos = gun, Dir = Vector3.Normalize(target - gun), Life = 1.4f });
            _fireCooldown = FireDelay;
            if (_stage == Stage.Trench)
                _trenchShots++;
        }
    }

    /// <summary>The ship drifts towards wherever you're aiming (surface and trench only).</summary>
    void FollowAim(float dt, float baseY, float minX, float maxX, float minY, float maxY, float xScale, float yScale)
    {
        float tx = Math.Clamp(_aim.X * xScale, minX, maxX);
        float ty = Math.Clamp(baseY - _aim.Y * yScale, minY, maxY);
        float k = MathF.Min(1f, 2.6f * dt);
        _cam.X += (tx - _cam.X) * k;
        _cam.Y += (ty - _cam.Y) * k;
    }

    void UpdateSpace(float dt)
    {
        _stageTime += dt;
        bool quotaMet = _kills >= _quota || _stageTime > 80f;

        if (!quotaMet)
        {
            int alive = _ents.Count(e => e.Kind == EKind.Interceptor);
            _spawnTimer -= dt;
            if (_spawnTimer <= 0 && alive < 3)
            {
                SpawnInterceptor();
                _spawnTimer = Range(1.4f, 2.8f);
            }
            return;
        }

        // enough shot down: the rest break off
        foreach (var e in _ents.Where(e => e.Kind == EKind.Interceptor && !e.Leaving))
        {
            e.Leaving = true;
            e.Vel = new Vector3(Range(-200f, 200f), Range(-120f, 120f), EnemySpeed * 1.4f);
        }

        if (!_ents.Any(e => e.Kind == EKind.Interceptor))
        {
            _endDelay += dt;
            if (_endDelay > 1.6f)
                StartSurface();
        }
    }

    void UpdateSurface(float dt)
    {
        _stageTime += dt;
        _cam.Z += SurfaceSpeed * dt;
        FollowAim(dt, 150f, -520f, 520f, 36f, 330f, 0.55f, 0.45f);

        foreach (var t in _towers)
        {
            if (!t.Hit && _camPrev.Z < t.Z && _cam.Z >= t.Z && MathF.Abs(_cam.X - t.X) < 26f && _cam.Y < t.Height + 10f)
            {
                t.Hit = true;
                HitPlayer();
            }
        }

        if (_cam.Z >= _surfaceLen)
            EndSurface();
    }

    void UpdateTrench(float dt)
    {
        _stageTime += dt;
        _cam.Z += TrenchSpeed * dt;
        FollowAim(dt, TH * 0.5f, -TW + 30f, TW - 30f, 28f, TH - 32f, 0.5f, 0.38f);

        foreach (var tr in _triggers)
        {
            if (tr.Done || _cam.Z < tr.Z || _portActive)
                continue;
            tr.Done = true;
            if (tr.Type == 0)
            {
                for (int i = 0; i < 2; i++)
                    SpawnTrenchInterceptor();
            }
            else
            {
                _ents.Add(new Ent
                {
                    Kind = EKind.Dark,
                    Pos = new Vector3(0, TH * 0.5f, _cam.Z + 1800f),
                    Radius = 44f,
                    FireTimer = 1.2f,
                    Sway = Rand() * 6f,
                });
                Banner("DARK FIGHTER", "it can't be destroyed - drive it off");
            }
        }

        foreach (var c in _catwalks)
        {
            if (!c.Hit && _camPrev.Z < c.Z && _cam.Z >= c.Z && MathF.Abs(_cam.Y - c.Y) < 34f)
            {
                c.Hit = true;
                HitPlayer();
            }
        }

        if (!_portActive && _cam.Z >= _trenchLen - PortRange)
        {
            _portActive = true;
            foreach (var e in _ents.Where(e => e.Kind is EKind.Dark or EKind.Interceptor))
            {
                e.Leaving = true;
                e.Vel = new Vector3(0, 80f, 1500f);
            }
            Banner("TORPEDO ARMED", "put it in the reactor port - SPACE to fire");
        }

        if (_portActive && _cam.Z >= _trenchLen - 220f)
            MissedRun();
    }

    void SpawnTrenchInterceptor()
    {
        Vector3 pos = new(Range(-TW + 40f, TW - 40f), Range(50f, TH - 50f), _cam.Z + Range(2400f, 3000f));
        Vector3 aim = new(_cam.X + Range(-70f, 70f), _cam.Y + Range(-50f, 50f), _cam.Z - 40f);
        _ents.Add(new Ent
        {
            Kind = EKind.Interceptor,
            Pos = pos,
            Aim = aim,
            Vel = Vector3.Normalize(aim - pos) * EnemySpeed,
            Radius = 30f,
            FireTimer = Range(0.8f, 2f),
            Sway = Rand() * 6f,
        });
    }

    void UpdateVictory(float dt)
    {
        _victoryTimer += dt;
        _cam.Z = MathF.Min(_cam.Z + TrenchSpeed * 0.2f * dt, _trenchLen - 400f);
        if (_victoryTimer > 5.5f)
        {
            _wave++;
            _shields = Math.Min(MaxShields, _shields + 1); // blowing up the station restores a shield
            StartSpace();
        }
    }

    // ------------------------------------------------------------------ entities

    void UpdateEntities(float dt)
    {
        for (int i = _ents.Count - 1; i >= 0; i--)
        {
            var e = _ents[i];
            bool remove = false;
            float dz = e.Pos.Z - _cam.Z;

            switch (e.Kind)
            {
                case EKind.Interceptor:
                {
                    e.Age += dt;
                    if (!e.Leaving)
                    {
                        Vector3 desired = Vector3.Normalize(e.Aim - e.Pos) * EnemySpeed;
                        e.Vel = Lerp(e.Vel, desired, MathF.Min(1f, 2f * dt));
                        e.Pos.X += MathF.Sin(e.Age * 1.7f + e.Sway) * 45f * dt;
                        e.Pos.Y += MathF.Cos(e.Age * 1.3f + e.Sway) * 32f * dt;
                        if (_stage == Stage.Trench)
                            e.Aim = new Vector3(_cam.X, _cam.Y, _cam.Z - 40f);

                        e.FireTimer -= dt;
                        if (e.FireTimer <= 0 && dz > 260f && dz < 2400f && _stage != Stage.Victory)
                        {
                            SpawnFireball(e.Pos);
                            e.FireTimer = Range(1.3f, 2.9f) * FireScale;
                        }
                    }
                    e.Pos += e.Vel * dt;
                    if (_stage == Stage.Trench)
                    {
                        e.Pos.X = Math.Clamp(e.Pos.X, -TW + 22f, TW - 22f);
                        e.Pos.Y = Math.Clamp(e.Pos.Y, 28f, TH - 28f);
                    }
                    if (dz < -80f || dz > 4200f)
                        remove = true;
                    break;
                }

                case EKind.Dark:
                {
                    e.Age += dt;
                    if (e.Leaving)
                    {
                        e.Pos += e.Vel * dt;
                        if (dz > 3200f || dz < -200f)
                            remove = true;
                        break;
                    }
                    // hovers ahead of you, weaving from side to side
                    float tx = MathF.Sin(e.Age * 0.8f + e.Sway) * TW * 0.55f;
                    float ty = TH * 0.5f + MathF.Sin(e.Age * 0.6f + e.Sway) * 70f;
                    e.Pos.X += (tx - e.Pos.X) * MathF.Min(1f, 1.5f * dt);
                    e.Pos.Y += (ty - e.Pos.Y) * MathF.Min(1f, 1.5f * dt);
                    e.Pos.Z = _cam.Z + 800f + MathF.Sin(e.Age * 0.5f) * 150f;
                    e.FireTimer -= dt;
                    if (e.FireTimer <= 0)
                    {
                        SpawnFireball(e.Pos);
                        e.FireTimer = Range(0.9f, 1.8f) * FireScale;
                    }
                    break;
                }

                case EKind.Fireball:
                {
                    Vector3 before = e.Pos;
                    float zBefore = before.Z - _camPrev.Z;
                    e.Pos += e.Vel * dt;
                    e.Spin += dt * 7f;
                    e.Age += dt;
                    float zAfter = e.Pos.Z - _cam.Z;

                    if (zBefore > 0 && zAfter <= 0 && _stage != Stage.Victory && _stage != Stage.GameOver)
                    {
                        float t = zBefore / (zBefore - zAfter);
                        Vector3 pe = Lerp(before, e.Pos, t), pc = Lerp(_camPrev, _cam, t);
                        if (MathF.Abs(pe.X - pc.X) < ShipHitX && MathF.Abs(pe.Y - pc.Y) < ShipHitY)
                        {
                            HitPlayer();
                            remove = true;
                        }
                    }
                    if (zAfter < -80f || e.Age > 9f)
                        remove = true;
                    break;
                }

                case EKind.Bunker:
                case EKind.Turret:
                {
                    e.FireTimer -= dt;
                    if (e.FireTimer <= 0 && dz > 350f && dz < 2100f)
                    {
                        SpawnFireball(e.Pos + new Vector3(0, e.Kind == EKind.Bunker ? 17f : 0f, 20f));
                        e.FireTimer = Range(1.8f, 4.2f) * FireScale;
                    }
                    if (dz < -150f)
                        remove = true;
                    break;
                }

                case EKind.TowerTop:
                    if (dz < -150f)
                        remove = true;
                    break;
            }

            if (remove)
                _ents.RemoveAt(i);
        }
    }

    // ---------------------------------------------------------------------- shots

    static float ClosestApproach(Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        Vector3 ab = b - a;
        float len2 = ab.LengthSquared();
        t = len2 < 1e-6f ? 0f : Math.Clamp(Vector3.Dot(c - a, ab) / len2, 0f, 1f);
        return (a + ab * t - c).Length();
    }

    void UpdateBolts(float dt)
    {
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i];
            float speed = b.Torpedo ? TorpedoSpeed : LaserSpeed;
            Vector3 a = b.Pos;
            Vector3 c = a + b.Dir * speed * dt;
            b.Life -= dt;
            bool remove = b.Life <= 0;

            if (b.Torpedo)
            {
                if (a.Z < _trenchLen && c.Z >= _trenchLen)
                {
                    float t = (_trenchLen - a.Z) / (c.Z - a.Z);
                    Vector3 hit = Lerp(a, c, t);
                    remove = true;
                    if (_stage == Stage.Trench && MathF.Abs(hit.X) <= 22f && MathF.Abs(hit.Y - 14f) <= 20f)
                        PortDestroyed();
                    else
                        Explode(hit, 12, Amber, 120f);
                }
            }
            else
            {
                // nearest thing the bolt passes through this frame
                Ent? best = null;
                float bestT = 2f;
                foreach (var e in _ents)
                {
                    if (e.Leaving && e.Kind != EKind.Dark)
                        continue;
                    float d = ClosestApproach(a, c, e.Pos, out float t);
                    if (d <= e.Radius && t < bestT)
                    {
                        best = e;
                        bestT = t;
                    }
                }
                if (best != null)
                {
                    HitEnemy(best, Lerp(a, c, bestT));
                    remove = true;
                }
            }

            b.Pos = c;
            if (remove)
                _bolts.RemoveAt(i);
        }
    }

    void HitEnemy(Ent e, Vector3 at)
    {
        switch (e.Kind)
        {
            case EKind.Fireball:
                _score += FireballPoints;
                Explode(e.Pos, 6, Amber, 90f);
                _ents.Remove(e);
                break;

            case EKind.Interceptor:
                _score += InterceptorPoints;
                _kills++;
                Explode(e.Pos, 22, White, 240f);
                _ents.Remove(e);
                break;

            case EKind.Dark:
                _score += DarkHitPoints;
                e.Hits++;
                Explode(at, 5, Red, 100f);
                if (e.Hits >= DarkHitsNeeded)
                {
                    e.Leaving = true;
                    e.Vel = new Vector3(0, 120f, 1500f);
                    Banner("DARK FIGHTER DRIVEN OFF", "");
                }
                break;

            case EKind.Bunker:
                _score += BunkerPoints;
                Explode(e.Pos, 14, Amber, 160f);
                _ents.Remove(e);
                break;

            case EKind.Turret:
                _score += TurretPoints;
                Explode(e.Pos, 10, Amber, 140f);
                _ents.Remove(e);
                break;

            case EKind.TowerTop:
                _topsKilled++;
                _score += 200 * _topsKilled; // 200 for the first, 400 for the second, ...
                Explode(e.Pos, 14, Amber, 160f);
                if (e.Tower != null) e.Tower.TopAlive = false;
                _ents.Remove(e);
                break;
        }
        _high = Math.Max(_high, _score);
    }

    void UpdateBits(float dt)
    {
        for (int i = _bits.Count - 1; i >= 0; i--)
        {
            var b = _bits[i];
            b.Pos += b.Vel * dt;
            b.Life -= dt;
            if (b.Life <= 0)
                _bits.RemoveAt(i);
        }
    }

    // ============================================================================ draw

    public void Draw()
    {
        _w = Gfx.Width;
        _h = Gfx.Height;
        _f = _h * 1.05f;
        _cx = _w / 2f;
        _cy = _h / 2f;
        _view = _cam + _shake;

        switch (_stage)
        {
            case Stage.Space:
                DrawStars(true);
                DrawStation();
                break;
            case Stage.Surface:
                DrawStars(false);
                DrawSurface();
                break;
            default:
                DrawTrench();
                break;
        }

        foreach (var e in _ents) DrawEntity(e);
        foreach (var b in _bolts) DrawBolt(b);
        foreach (var b in _bits) DrawBit(b);

        DrawReticle();
        DrawHud();

        if (_flash > 0)
            Gfx.FillRect(0, 0, _w, _h, Color.FromArgb((int)(Math.Clamp(_flash, 0f, 1f) * 90f), 255, _stage == Stage.Victory ? 255 : 50, _stage == Stage.Victory ? 255 : 40));
    }

    static Color Fog(Color c, float z) => Gfx.Fade(c, Math.Clamp(1f - z / DrawFar * 0.85f, 0.14f, 1f));

    Vector2 Project(Vector3 c) => new(_cx + _f * c.X / c.Z, _cy - _f * c.Y / c.Z);

    /// <summary>Draws a line given in camera-relative coordinates, clipped against the near plane.</summary>
    void LineRel(Vector3 a, Vector3 b, Color col, float width = 1.5f)
    {
        if (a.Z < Near && b.Z < Near)
            return;
        if (a.Z < Near)
            a += (b - a) * ((Near - a.Z) / (b.Z - a.Z));
        else if (b.Z < Near)
            b += (a - b) * ((Near - b.Z) / (a.Z - b.Z));
        Gfx.Line(Project(a), Project(b), width, col);
    }

    void LineWorld(Vector3 a, Vector3 b, Color col, float width = 1.5f) =>
        LineRel(a - _view, b - _view, col, width);

    void DrawModel(Model m, Vector3 pos, float yaw, float roll, Color color, float scale = 1f)
    {
        Vector3 rel = pos - _view;
        if (rel.Z + m.Radius * scale + 60f < Near || rel.Z > DrawFar)
            return;

        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw), cr = MathF.Cos(roll), sr = MathF.Sin(roll);
        Color col = Fog(color, rel.Z);
        Vector3 T(Vector3 p)
        {
            p *= scale;
            float x = p.X * cr - p.Y * sr, y = p.X * sr + p.Y * cr;
            return rel + new Vector3(x * cy + p.Z * sy, y, -x * sy + p.Z * cy);
        }

        foreach (var (a, b) in m.Edges)
            LineRel(T(a), T(b), col);
    }

    void DrawEntity(Ent e)
    {
        switch (e.Kind)
        {
            case EKind.Interceptor:
                DrawModel(Models.Interceptor, e.Pos, e.Leaving ? 0f : MathF.PI, MathF.Sin(e.Age * 1.4f + e.Sway) * 0.35f, White);
                break;
            case EKind.Dark:
                DrawModel(Models.DarkFighter, e.Pos, e.Leaving ? 0f : MathF.PI, MathF.Sin(e.Age) * 0.2f, Red);
                break;
            case EKind.Fireball:
                DrawModel(Models.Fireball, e.Pos, e.Spin, e.Spin * 0.7f, Amber);
                break;
            case EKind.Bunker:
                DrawModel(Models.Bunker, e.Pos, MathF.PI, 0, Red);
                break;
            case EKind.TowerTop:
                DrawModel(Models.TowerTop, e.Pos, MathF.PI, 0, Red);
                break;
            case EKind.Turret:
                DrawModel(Models.Turret, e.Pos, MathF.PI, 0, Red);
                break;
        }
    }

    void DrawBolt(Bolt b)
    {
        if (b.Torpedo)
        {
            Vector3 tail = b.Pos - b.Dir * 40f;
            LineWorld(tail, b.Pos, Cyan, 3.5f);
            return;
        }
        LineWorld(b.Pos - b.Dir * 80f, b.Pos, Green, 2.4f);
    }

    void DrawBit(Bit b)
    {
        Vector3 half = b.Dir * (b.Len / 2f);
        Color c = Gfx.Fade(b.Color, b.Life / b.MaxLife);
        LineWorld(b.Pos - half, b.Pos + half, c, 1.4f);
    }

    // ----------------------------------------------------------------- backdrops

    void DrawStars(bool wholeSky)
    {
        // The ship is on rails, so the stars barely move; in the surface stage only the half above the horizon shows.
        float maxY = wholeSky ? _h : _cy - 2f;
        foreach (var s in _stars)
        {
            float y = s.Y * _h;
            if (y > maxY)
                continue;
            Gfx.FillRect(s.X * _w, y, 1.7f, 1.7f, Gfx.Fade(White, s.Z * 0.8f));
        }
    }

    /// <summary>The battle station: a growing ball with an equatorial trench, ahead and to one side.</summary>
    void DrawStation()
    {
        float progress = _quota > 0 ? Math.Clamp((float)_kills / _quota, 0f, 1f) : 0f;
        float r = 55f + 130f * progress;
        var c = new Vector2(_cx - _w * 0.26f, _cy - _h * 0.2f);
        Gfx.CircleOutline(c, r, Dim);

        // equator (the trench) and a meridian
        Vector2 prevA = default, prevB = default, prevC = default;
        const int n = 28;
        for (int i = 0; i <= n; i++)
        {
            float a = -MathF.PI / 2f + i * MathF.PI / n;
            var eq = c + new Vector2(MathF.Sin(a) * r, MathF.Cos(a) * r * 0.22f);
            var eq2 = c + new Vector2(MathF.Sin(a) * r, MathF.Cos(a) * r * 0.22f - 6f);
            var mer = c + new Vector2(MathF.Sin(a) * r * 0.35f, -MathF.Cos(a) * r);
            if (i > 0)
            {
                Gfx.Line(prevA, eq, 1f, Dim);
                Gfx.Line(prevB, eq2, 1f, Dim);
                Gfx.Line(prevC, mer, 1f, Dim);
            }
            prevA = eq;
            prevB = eq2;
            prevC = mer;
        }

        // the dish
        Gfx.CircleOutline(c + new Vector2(-r * 0.38f, -r * 0.4f), r * 0.2f, Dim);
        Gfx.CircleOutline(c + new Vector2(-r * 0.38f, -r * 0.4f), r * 0.08f, Dim);
    }

    void DrawSurface()
    {
        // grid of the station's surface
        Color grid = Blue;
        for (int k = -14; k <= 14; k++)
        {
            float x = k * 100f;
            for (int s = 0; s < 8; s++)
            {
                float z0 = _view.Z + Near + s * 400f, z1 = _view.Z + Near + (s + 1) * 400f;
                LineWorld(new Vector3(x, 0, z0), new Vector3(x, 0, z1), Fog(grid, z0 - _view.Z), 1.2f);
            }
        }
        for (float z = MathF.Ceiling((_view.Z + 20f) / 150f) * 150f; z < _view.Z + DrawFar; z += 150f)
            LineWorld(new Vector3(-1400, 0, z), new Vector3(1400, 0, z), Fog(grid, z - _view.Z), 1.2f);

        foreach (var d in _decos)
            DrawModel(d.Model, d.Pos, 0, 0, Dim);

        foreach (var t in _towers)
            DrawModel(t.Pole, new Vector3(t.X, 0, t.Z), 0, 0, Red);
    }

    void DrawTrench()
    {
        float zFirst = MathF.Floor(_view.Z / 100f) * 100f;
        float zLast = MathF.Min(_view.Z + DrawFar, _trenchLen);

        // cross-sections every 100 units
        for (float z = zFirst; z <= zLast; z += 100f)
        {
            float dz = z - _view.Z;
            if (dz < Near)
                continue;
            Color c = Fog(Blue, dz);
            LineWorld(new Vector3(-TW, 0, z), new Vector3(TW, 0, z), c, 1.2f);
            LineWorld(new Vector3(-TW, 0, z), new Vector3(-TW, TH, z), c, 1.2f);
            LineWorld(new Vector3(TW, 0, z), new Vector3(TW, TH, z), c, 1.2f);

            // wall panels, picked pseudo-randomly from the section number so they stay put
            int idx = (int)(z / 100f);
            int h1 = (idx * 7919 + 13) % 11, h2 = (idx * 104729 + 7) % 13;
            Color pc = Fog(Cyan, dz);
            if (h1 < 4)
                Panel(-TW, z, 30f + h1 * 20f, 70f + h1 * 15f, pc);
            if (h2 < 5)
                Panel(TW, z, 40f + h2 * 18f, 60f + h2 * 14f, pc);
        }

        // long edges, drawn in 400-unit pieces so they can fade with distance
        float[] wallY = { 0f, TH * 0.25f, TH * 0.5f, TH * 0.75f, TH };
        for (float z = MathF.Floor(_view.Z / 400f) * 400f; z < zLast; z += 400f)
        {
            float za = MathF.Max(z, _view.Z + Near), zb = MathF.Min(z + 400f, _trenchLen);
            if (zb <= za)
                continue;
            Color c = Fog(Blue, za - _view.Z);
            foreach (float y in wallY)
            {
                LineWorld(new Vector3(-TW, y, za), new Vector3(-TW, y, zb), c, 1.2f);
                LineWorld(new Vector3(TW, y, za), new Vector3(TW, y, zb), c, 1.2f);
            }
            foreach (float x in new[] { -TW * 0.5f, 0f, TW * 0.5f })
                LineWorld(new Vector3(x, 0, za), new Vector3(x, 0, zb), c, 1.2f);
        }

        foreach (var c in _catwalks)
        {
            if (c.Z - _view.Z > -60f)
                DrawModel(CatwalkModel, new Vector3(0, c.Y, c.Z), 0, 0, Amber);
        }

        DrawEndWall();
    }

    static readonly Model CatwalkModel = Models.Catwalk(TW);

    void Panel(float wallX, float z, float y0, float y1, Color c)
    {
        float x = wallX > 0 ? wallX - 1f : wallX + 1f;
        float zEnd = z + 60f;
        LineWorld(new Vector3(x, y0, z + 10f), new Vector3(x, y0, zEnd), c, 1.1f);
        LineWorld(new Vector3(x, y1, z + 10f), new Vector3(x, y1, zEnd), c, 1.1f);
        LineWorld(new Vector3(x, y0, z + 10f), new Vector3(x, y1, z + 10f), c, 1.1f);
        LineWorld(new Vector3(x, y0, zEnd), new Vector3(x, y1, zEnd), c, 1.1f);
    }

    /// <summary>The wall at the end of the trench, with the reactor port at the bottom.</summary>
    void DrawEndWall()
    {
        float dz = _trenchLen - _view.Z;
        if (dz > DrawFar)
            return;
        Color c = Fog(Cyan, dz);
        Vector3 a = new(-TW, 0, _trenchLen), b = new(TW, 0, _trenchLen), cc = new(TW, TH, _trenchLen), d = new(-TW, TH, _trenchLen);
        LineWorld(a, b, c);
        LineWorld(b, cc, c);
        LineWorld(cc, d, c);
        LineWorld(d, a, c);
        LineWorld(a, cc, Fog(Blue, dz), 1f);
        LineWorld(b, d, Fog(Blue, dz), 1f);
        DrawModel(Models.Port, new Vector3(0, 0, _trenchLen), 0, 0, _stage == Stage.Victory ? White : Red);
    }

    // ------------------------------------------------------------------ cross-hair and HUD

    void DrawReticle()
    {
        if (_stage == Stage.GameOver || _stage == Stage.Victory)
            return;

        var p = new Vector2(_cx + _aim.X, _cy + _aim.Y);
        Color c = _portActive ? Cyan : Green;
        Gfx.Line(p + new Vector2(-26, 0), p + new Vector2(-9, 0), 1.8f, c);
        Gfx.Line(p + new Vector2(9, 0), p + new Vector2(26, 0), 1.8f, c);
        Gfx.Line(p + new Vector2(0, -26), p + new Vector2(0, -9), 1.8f, c);
        Gfx.Line(p + new Vector2(0, 9), p + new Vector2(0, 26), 1.8f, c);
        Gfx.Rect(p.X - 9, p.Y - 9, 18, 18, Gfx.Fade(c, 0.6f));

        if (_portActive)
        {
            // the targeting box around the reactor port
            Vector3 rel = new Vector3(0, 14f, _trenchLen) - _view;
            if (rel.Z > Near)
            {
                Vector2 s = Project(rel);
                float size = MathF.Max(14f, _f * 40f / rel.Z);
                if ((_time * 5f % 1f) < 0.7f)
                {
                    float L = size * 0.4f;
                    foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    {
                        var corner = s + new Vector2(sx * size, sy * size);
                        Gfx.Line(corner, corner - new Vector2(sx * L, 0), 2f, Amber);
                        Gfx.Line(corner, corner - new Vector2(0, sy * L), 2f, Amber);
                    }
                }
            }
        }
    }

    void DrawHud()
    {
        int w = (int)_w, h = (int)_h;

        Gfx.Text($"SCORE  {_score:D7}", 20, 14, 24, Cyan);
        Gfx.Text($"HIGH   {_high:D7}", 20, 44, 18, Dim);
        string wave = $"WAVE {_wave}";
        Gfx.Text(wave, w - Gfx.Measure(wave, 24) - 20, 14, 24, Cyan);

        string stage = _stage switch
        {
            Stage.Space => $"DOGFIGHT   {Math.Min(_kills, _quota)}/{_quota}",
            Stage.Surface => _topsTotal > 0 ? $"SURFACE   TOWER TOPS {_topsKilled}/{_topsTotal}" : "SURFACE",
            Stage.Trench or Stage.Victory => "TRENCH",
            _ => "",
        };
        Gfx.Text(stage, w - Gfx.Measure(stage, 18) - 20, 46, 18, Dim);

        // progress bar for the surface and trench runs
        if (_stage is Stage.Surface or Stage.Trench)
        {
            float frac = _stage == Stage.Surface ? _cam.Z / _surfaceLen : _cam.Z / _trenchLen;
            float bx = _cx - 150, by = h - 62;
            Gfx.Rect(bx, by, 300, 8, Dim);
            Gfx.FillRect(bx + 1, by + 1, 298 * Math.Clamp(frac, 0f, 1f), 6, _portActive ? Amber : Dim);
        }

        // shields
        Gfx.Text("DEFLECTOR SHIELDS", (int)_cx - Gfx.Measure("DEFLECTOR SHIELDS", 14) / 2, h - 50, 14, Dim);
        for (int i = 0; i < MaxShields; i++)
        {
            float x = _cx - MaxShields * 18f + i * 36f;
            Color sc = i < _shields ? (_shields <= 2 ? Red : Cyan) : Dim;
            if (i < _shields)
                Gfx.FillRect(x, h - 32, 30, 12, sc);
            else
                Gfx.Rect(x, h - 32, 30, 12, sc);
        }

        Gfx.Text("Arrows/WASD aim and steer   SPACE fire   P pause   ESC menu", 20, h - 24, 14, Dim);

        // banners
        if (_bannerTimer > 0 && _stage != Stage.GameOver && _stage != Stage.Victory)
        {
            Centered(_banner, 120, 36, Cyan);
            if (_subBanner.Length > 0)
                Centered(_subBanner, 166, 20, Amber);
        }

        if (_portActive && _stage == Stage.Trench && !_torpedoFired && (_time * 4f % 1f) < 0.65f)
            Centered("FIRE THE TORPEDO!", 220, 28, Amber);

        if (_stage == Stage.Victory)
        {
            Centered("BATTLE STATION DESTROYED", 100, 40, Cyan);
            for (int i = 0; i < _victoryLines.Count; i++)
                Centered(_victoryLines[i], 160 + i * 30, 22, i == 1 ? Green : Amber);
            if (_victoryTimer > 3f)
                Centered("+1 SHIELD    NEXT WAVE...", 160 + _victoryLines.Count * 30 + 10, 20, Cyan);
        }

        if (_paused)
            Centered("PAUSED", h / 2 - 20, 40, Cyan);

        if (_stage == Stage.GameOver)
        {
            Centered("GAME OVER", h / 2 - 70, 56, Red);
            Centered($"Final score {_score}", h / 2 - 8, 24, Amber);
            Centered("Press ENTER to play again", h / 2 + 36, 22, Cyan);
        }
    }

    static void Centered(string text, int y, int size, Color color) =>
        Gfx.Text(text, (Gfx.Width - Gfx.Measure(text, size)) / 2, y, size, color);
}
