using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using WireGames;

namespace LunarLander;

/// <summary>
/// Wireframe Lunar Lander with a ship-centric camera: the lander is always drawn at the centre of
/// the screen, upright, and the moon scrolls past underneath it. World coordinates are x = along the
/// surface (wraps around), y = up.
///
/// Rotation isn't implemented yet, so sideways movement comes from small side thrusters.
/// </summary>
public sealed class LanderGame : IGame
{
    // ---- terrain: a ring of N height samples, Step units apart --------------------------------
    const int N = 256;
    const float Step = 24f;
    const float Length = N * Step; // 6144 units around the moon
    const float Grid = 128f;       // divides Length evenly, so the marker lattice wraps seamlessly

    // ---- flight model (units: world units / second) ------------------------------------------
    const float Gravity = 55f;
    const float MainThrust = 135f;     // while UP is held
    const float SideThrust = 55f;      // while LEFT / RIGHT is held
    const float MainBurn = 14f;        // fuel/s while boosting
    const float SideBurn = 3f;
    const float StartFuel = 1000f;
    const float MaxFuel = 1500f;
    const float LandingFuelBonus = 300f;

    // ---- landing limits ------------------------------------------------------------------------
    const float MaxLandingVy = 38f;
    const float MaxLandingVx = 20f;

    // ---- mothership ------------------------------------------------------------------------------
    const float MothershipAltitude = 850f; // above the highest point of the terrain
    const float MothershipSpeed = 18f;     // it orbits at a steady speed, left or right
    const float HullHalf = 110f;           // half the hull width
    const float HullHeight = 56f;
    const float PortHalf = 26f;           // half the width of the docking bay in the hull's underside
    const float LanderTop = 18f;           // top of the lander's cabin above its centre
    const float SteerAccel = 170f;         // while docked, LEFT/RIGHT slide the mothership (the lander manages 55)
    const float SteerMaxSpeed = 280f;
    const float SteerRelax = 150f;         // how fast it settles back to its orbit speed once the key is released
    const float MaxDockRelVx = 14f;        // speed relative to the mothership
    const float MaxDockVy = 32f;
    const float DockBonus = 150f;

    // ---- lander geometry (offsets from the lander's centre, y up) ------------------------------
    const float FootDx = 15f;
    const float FootDy = 16f; // feet are this far below the centre

    static readonly Color Green = Color.FromArgb(255, 120, 255, 150);
    static readonly Color Amber = Color.FromArgb(255, 255, 190, 80);
    static readonly Color Red = Color.FromArgb(255, 255, 90, 80);
    static readonly Color Rock = Color.FromArgb(255, 225, 235, 255);
    static readonly Color Dim = Color.FromArgb(255, 70, 90, 80);
    static readonly Color Cyan = Color.FromArgb(255, 120, 215, 255);

    sealed record Pad(int Start, int Count, int Multiplier)
    {
        public float X0 => Start * Step;
        public float X1 => (Start + Count - 1) * Step;
    }

    sealed class Piece
    {
        public Vector2 Pos, Vel;
        public float Angle, Spin, Length, Life, MaxLife;
    }

    /// <summary>Rounds alternate: fly down from the mothership, then fly back up to it.</summary>
    enum Round { Descent, Ascent }

    enum State { Undocking, Flying, Landed, Docked, Crashed, GameOver }

    readonly Random _rng = new();
    readonly float[] _h = new float[N];
    readonly List<Pad> _pads = new();
    readonly List<Piece> _pieces = new();

    // lander state (world space)
    float _x, _y;
    Vector2 _vel;
    bool _boost;
    bool _sideLeft, _sideRight;
    float _fuel;
    bool _onGround;              // sitting on a pad (ascent rounds start like this)
    float _sitX, _sitY;          // where we last touched down, to restart an ascent from

    // mothership: its docking port is at (_msX, _msY); it moves along at _msVx
    float _msX, _msY, _msVx;
    float _maxH;                 // highest terrain point (the mothership flies above it)
    float _msOrbitVx;            // the speed it settles back to when nobody is steering it
    int _msSteer;                // -1 / 0 / +1: is the mothership being steered right now (for the exhaust puffs)

    Round _round;
    State _state;
    bool _paused;
    int _score, _lives, _level, _roundNo;
    float _flightTime;
    float _time;
    int _lastAward;
    int _lastMult;

    Vector2 _c; // screen centre, set every Draw

    public LanderGame() => NewGame();

    // =========================================================================== setup

    void NewGame()
    {
        _score = 0;
        _lives = 3;
        _level = 1;
        _roundNo = 1;
        _fuel = StartFuel;
        BeginDescent(newTerrain: true);
    }

    void ResetControls()
    {
        _pieces.Clear();
        _boost = false;
        _sideLeft = _sideRight = false;
        _flightTime = 0;
    }

    void ResetMothership(float x, float side)
    {
        _msX = WrapX(x);
        _msY = _maxH + MothershipAltitude;
        _msOrbitVx = side * MothershipSpeed;
        _msVx = _msOrbitVx;
        _msSteer = 0;
    }

    /// <summary>The lander starts docked to the mothership, then is released to fall towards the moon.</summary>
    void BeginDescent(bool newTerrain)
    {
        if (newTerrain)
            GenerateTerrain();

        ResetControls();
        _round = Round.Descent;
        _onGround = false;
        ResetMothership(Rand() * Length, Rand() < 0.5f ? -1f : 1f);

        _x = _msX;
        _y = _msY - LanderTop;
        _vel = new Vector2(_msVx, 0f);
        _state = State.Undocking;
    }

    /// <summary>The lander is sitting on the pad it landed on; the mothership is somewhere overhead.</summary>
    void BeginAscent()
    {
        ResetControls();
        _round = Round.Ascent;
        _onGround = true;
        _x = _sitX;
        _y = _sitY;
        _vel = Vector2.Zero;

        float side = Rand() < 0.5f ? -1f : 1f;
        ResetMothership(_sitX + side * (700f + Rand() * 1700f), Rand() < 0.5f ? -1f : 1f);
        _state = State.Flying;
    }

    void GenerateTerrain()
    {
        // Periodic midpoint displacement
        Array.Clear(_h);
        _h[0] = Rand() * 120f;
        _h[N / 2] = Rand() * 120f;
        float amp = 240f;
        float roughness = 0.52f + MathF.Min(_level - 1, 6) * 0.02f; // gets craggier each level
        for (int step = N / 2; step > 1; step /= 2)
        {
            int half = step / 2;
            for (int i = 0; i < N; i += step)
            {
                float a = _h[i], b = _h[(i + step) % N];
                _h[i + half] = (a + b) / 2 + (Rand() * 2 - 1) * amp;
            }
            amp *= roughness;
        }

        // Flat landing pads: narrower = harder = worth more
        _pads.Clear();
        int[] counts = { 6, 5, 4, 3 };
        int[] mults = { 1, 2, 3, 5 };
        for (int p = 0; p < counts.Length; p++)
        {
            int start;
            int tries = 0;
            do
            {
                start = 6 + _rng.Next(N - 12 - counts[p]);
                tries++;
            } while (tries < 200 && _pads.Any(o => start < o.Start + o.Count + 10 && o.Start < start + counts[p] + 10));

            for (int i = 1; i < counts[p]; i++)
                _h[start + i] = _h[start];
            _pads.Add(new Pad(start, counts[p], mults[p]));
        }

        _maxH = _h.Max();
    }

    // ========================================================================= helpers

    float Rand() => (float)_rng.NextDouble();

    static float WrapX(float x) => x - Length * MathF.Floor(x / Length);

    static float DeltaX(float d) => d - Length * MathF.Floor(d / Length + 0.5f);

    float Height(float x)
    {
        float f = WrapX(x) / Step;
        int i = (int)f;
        float t = f - i;
        i %= N;
        int j = (i + 1) % N;
        return _h[i] + (_h[j] - _h[i]) * t;
    }

    /// <summary>The pad both feet are currently over, if any.</summary>
    Pad? PadUnderFeet()
    {
        float lx = _x - FootDx, rx = _x + FootDx;
        foreach (var pad in _pads)
        {
            float w = pad.X1 - pad.X0;
            if (WrapX(lx - pad.X0) <= w && WrapX(rx - pad.X0) <= w)
                return pad;
        }
        return null;
    }

    /// <summary>Gap between the lowest foot and the ground below it.</summary>
    float Altitude() =>
        MathF.Min(_y - FootDy - Height(_x - FootDx), _y - FootDy - Height(_x + FootDx));

    /// <summary>World point -> screen. The lander is always at the screen centre.</summary>
    Vector2 ToScreen(float wx, float wy) => new(_c.X + DeltaX(wx - _x), _c.Y - (wy - _y));

    // ========================================================================== update

    public void Update(float dt)
    {
        _time += dt;

        if (Input.Pressed(Keys.P) && _state == State.Flying)
            _paused = !_paused;
        if (_paused)
            return;

        UpdatePieces(dt);
        UpdateMothership(dt);

        switch (_state)
        {
            case State.Undocking:
                UpdateUndocking(dt);
                break;

            case State.Flying:
                UpdateFlight(dt);
                break;

            case State.Landed when Input.Pressed(Keys.Enter):
                // Landed: next up is the take-off and the climb back to the mothership.
                _roundNo++;
                _fuel = MathF.Min(MaxFuel, _fuel + LandingFuelBonus);
                BeginAscent();
                break;

            case State.Docked when Input.Pressed(Keys.Enter):
                // Docked: the lander is released again for a fresh descent onto a new stretch of moon.
                _roundNo++;
                _level++;
                BeginDescent(newTerrain: true);
                break;

            case State.Docked:
                FollowMothership();
                break;

            case State.Crashed when Input.Pressed(Keys.Enter):
                _fuel = MathF.Max(_fuel, 400f); // never retry with an empty tank
                if (_round == Round.Ascent)
                    BeginAscent();
                else
                    BeginDescent(newTerrain: false);
                break;

            case State.GameOver when Input.Pressed(Keys.Enter):
                NewGame();
                break;
        }
    }

    /// <summary>While docked the lander rides along in the mothership's port.</summary>
    void FollowMothership()
    {
        _x = _msX;
        _y = _msY - LanderTop;
        _vel = new Vector2(_msVx, 0f);
    }

    /// <summary>
    /// Moves the mothership. While the lander is still docked before a descent, LEFT/RIGHT steer it (much
    /// faster than the lander can move itself) so it can be lined up above a pad; otherwise it settles
    /// back to its steady orbit speed.
    /// </summary>
    void UpdateMothership(float dt)
    {
        _msSteer = 0;
        if (_state == State.Undocking)
        {
            if (Input.Down(Keys.Left) || Input.Down(Keys.A)) _msSteer -= 1;
            if (Input.Down(Keys.Right) || Input.Down(Keys.D)) _msSteer += 1;
        }

        if (_msSteer != 0)
        {
            _msVx = Math.Clamp(_msVx + _msSteer * SteerAccel * dt, -SteerMaxSpeed, SteerMaxSpeed);
        }
        else
        {
            float diff = _msOrbitVx - _msVx;
            float step = SteerRelax * dt;
            _msVx = MathF.Abs(diff) <= step ? _msOrbitVx : _msVx + MathF.Sign(diff) * step;
        }

        _msX = WrapX(_msX + _msVx * dt);
    }

    /// <summary>Docked before a descent: steer the mothership over a pad, then release the lander.</summary>
    void UpdateUndocking(float dt)
    {
        FollowMothership();
        if (Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space))
        {
            // Released with a small push away from the hull, still carrying the mothership's sideways speed.
            _vel = new Vector2(_msVx, -12f);
            _state = State.Flying;
        }
    }

    void UpdateFlight(float dt)
    {
        _flightTime += dt;

        // Every thruster is a boost that only fires while its key is held down.
        _boost = Input.Down(Keys.Up) || Input.Down(Keys.W) || Input.Down(Keys.Space);
        _sideLeft = Input.Down(Keys.Left) || Input.Down(Keys.A);
        _sideRight = Input.Down(Keys.Right) || Input.Down(Keys.D);

        if (_fuel <= 0)
        {
            _fuel = 0;
            _boost = false;
            _sideLeft = _sideRight = false;
        }

        if (_onGround)
        {
            // Sitting on a pad: stay put until the main engine is fired.
            _sideLeft = _sideRight = false;
            if (!_boost)
            {
                _vel = Vector2.Zero;
                return;
            }
            _onGround = false;
        }

        float ax = 0f;
        float ay = -Gravity + (_boost ? MainThrust : 0f);
        // "Left" pushes the lander left: thrust is applied from the right-hand jet.
        if (_sideLeft) ax -= SideThrust;
        if (_sideRight) ax += SideThrust;

        _fuel -= ((_boost ? MainBurn : 0f) + ((_sideLeft ? 1 : 0) + (_sideRight ? 1 : 0)) * SideBurn) * dt;

        _vel += new Vector2(ax, ay) * dt;
        _x = WrapX(_x + _vel.X * dt);
        _y += _vel.Y * dt;

        CheckContact();
        if (_state == State.Flying && _round == Round.Ascent)
            CheckDocking();
    }

    /// <summary>Ascent rounds: reaching the underside of the hull either docks (aligned and slow) or crashes.</summary>
    void CheckDocking()
    {
        float dx = DeltaX(_msX - _x);
        if (MathF.Abs(dx) > HullHalf)
            return;
        if (_y + LanderTop < _msY || _y > _msY + HullHeight)
            return; // still below the hull, or already above it

        bool aligned = MathF.Abs(dx) <= PortHalf - 6f;
        bool slow = MathF.Abs(_vel.X - _msVx) <= MaxDockRelVx && _vel.Y <= MaxDockVy;
        if (aligned && slow)
            Dock();
        else
            Crash();
    }

    void Dock()
    {
        _boost = false;
        _sideLeft = _sideRight = false;
        FollowMothership();
        _fuel = MathF.Max(_fuel, StartFuel); // the mothership tops us up
        _lastAward = (int)(DockBonus + _fuel / 10f);
        _score += _lastAward;
        _state = State.Docked;
    }

    void CheckContact()
    {
        // Anything other than the feet touching the ground is a crash.
        // Body points are offsets from the centre (y up).
        ReadOnlySpan<Vector2> body = stackalloc Vector2[]
        {
            new(-12, 4), new(12, 4), new(-10, 12), new(10, 12), new(0, 18),
        };
        foreach (var p in body)
        {
            if (_y + p.Y <= Height(_x + p.X))
            {
                Crash();
                return;
            }
        }

        bool leftDown = _y - FootDy <= Height(_x - FootDx);
        bool rightDown = _y - FootDy <= Height(_x + FootDx);
        if ((!leftDown && !rightDown) || _vel.Y > 0)
            return; // not touching, or still climbing

        var pad = PadUnderFeet();
        if (pad != null && MathF.Abs(_vel.X) <= MaxLandingVx && -_vel.Y <= MaxLandingVy)
        {
            if (_round == Round.Descent)
                Land(pad);
            else
                SitDown(); // set back down on a pad during the climb: safe, but no points
        }
        else
        {
            Crash();
        }
    }

    void SitDown()
    {
        _y = Height(_x) + FootDy;
        _vel = Vector2.Zero;
        _boost = false;
        _sideLeft = _sideRight = false;
        _onGround = true;
    }

    void Land(Pad pad)
    {
        SitDown();
        _sitX = _x;
        _sitY = _y;
        _lastMult = pad.Multiplier;
        _lastAward = 50 * pad.Multiplier + (int)(_fuel / 10f);
        _score += _lastAward;
        _state = State.Landed;
    }

    void Crash()
    {
        _boost = false;
        _sideLeft = _sideRight = false;
        _lives--;
        _state = _lives > 0 ? State.Crashed : State.GameOver;

        for (int i = 0; i < 16; i++)
        {
            float a = Rand() * MathF.Tau;
            float life = 1.2f + Rand() * 1.3f;
            _pieces.Add(new Piece
            {
                Pos = new Vector2(_x, _y),
                Vel = _vel * 0.4f + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (20f + Rand() * 110f),
                Angle = Rand() * MathF.Tau,
                Spin = (Rand() - 0.5f) * 10f,
                Length = 6f + Rand() * 16f,
                Life = life,
                MaxLife = life,
            });
        }
        _vel = Vector2.Zero;
    }

    void UpdatePieces(float dt)
    {
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];
            p.Vel.Y -= Gravity * dt;
            Vector2 next = p.Pos + p.Vel * dt;
            if (next.Y <= Height(next.X))
            {
                next.Y = Height(next.X);
                p.Vel = Vector2.Zero;
                p.Spin = 0;
            }
            p.Pos = new Vector2(WrapX(next.X), next.Y);
            p.Angle += p.Spin * dt;
            p.Life -= dt;
            if (p.Life <= 0)
                _pieces.RemoveAt(i);
        }
    }

    // ============================================================================ draw

    public void Draw()
    {
        _c = new Vector2(Gfx.Width / 2f, Gfx.Height / 2f);

        DrawMarkers();
        DrawTerrain();
        if (_round == Round.Ascent && _state == State.Flying)
            DrawDockingBeam();
        DrawMothership();
        DrawPieces();
        if (_state is not (State.Crashed or State.GameOver))
            DrawLander();
        DrawHud();
    }

    /// <summary>
    /// Approach guidance during an ascent: a funnel of light fanning out and down from the docking bay,
    /// with chevrons sliding up it towards the bay. Cyan while you're outside it, green once you're
    /// inside the funnel, and bright green with a solid centre line once you're lined up with the bay.
    /// </summary>
    void DrawDockingBeam()
    {
        const float beamLength = 900f;
        const float spread = 0.28f;   // funnel half-width grows by this much per unit of depth
        const float rungSpacing = 70f;
        const float rungSpeed = 110f; // how fast the chevrons slide up towards the bay

        float rel = DeltaX(_msX - _x);
        if (MathF.Abs(rel) > _c.X + beamLength * spread + HullHalf)
            return; // funnel is entirely off the side of the screen

        float depth = _msY - (_y + LanderTop);            // how far below the bay the lander's top is
        bool inFunnel = depth > 0 && MathF.Abs(rel) <= PortHalf + depth * spread;
        bool aligned = depth > -5 && MathF.Abs(rel) <= PortHalf - 6f;
        Color baseColor = aligned ? Green : inFunnel ? Green : Cyan;
        float strength = aligned ? 1f : inFunnel ? 0.8f : 0.55f;

        Vector2 P(float dx, float d) => ToScreen(_msX + dx, _msY - d); // d = distance below the bay

        // two edges of the funnel, fading out with distance
        const int segs = 10;
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < segs; i++)
            {
                float d0 = beamLength * i / segs, d1 = beamLength * (i + 1) / segs;
                float a = strength * (1f - d0 / beamLength);
                Line(P(side * (PortHalf + d0 * spread), d0), P(side * (PortHalf + d1 * spread), d1),
                    Gfx.Fade(baseColor, a), aligned ? 2.2f : 1.4f);
            }
        }

        // centre guide line: dashed until you're lined up, then solid
        for (float d = 0; d < beamLength; d += 30f)
        {
            if (!aligned && ((int)(d / 30f) & 1) == 1)
                continue;
            float a = strength * 0.7f * (1f - d / beamLength);
            Line(P(0, d), P(0, d + (aligned ? 31f : 22f)), Gfx.Fade(baseColor, a), 1.2f);
        }

        // chevrons sliding up the funnel, pointing at the bay
        float offset = (_time * rungSpeed) % rungSpacing;
        for (float d = offset; d < beamLength; d += rungSpacing)
        {
            float hw = PortHalf + d * spread;
            float a = strength * (1f - d / beamLength);
            Color c = Gfx.Fade(baseColor, a);
            Line(P(-hw, d), P(0, d - hw * 0.22f), c, 1.3f);
            Line(P(0, d - hw * 0.22f), P(hw, d), c, 1.3f);
        }
    }

    /// <summary>
    /// The mothership: a wireframe hull with a docking bay in its underside, orbiting overhead.
    /// Offsets are in world units from the docking port (y up).
    /// </summary>
    void DrawMothership()
    {
        float rel = DeltaX(_msX - _x);
        if (MathF.Abs(rel) > _c.X + HullHalf + 20f)
            return; // off the side of the screen
        float yTop = _c.Y - (_msY + HullHeight + 30f - _y);
        float yBottom = _c.Y - (_msY - 30f - _y);
        if (yBottom < 0 || yTop > Gfx.Height)
            return; // above or below the screen

        Vector2 P(float dx, float dy) => ToScreen(_msX + dx, _msY + dy);
        void L(float x0, float y0, float x1, float y1, float w = 1.6f) => Line(P(x0, y0), P(x1, y1), Cyan, w);

        // hull outline, with the docking bay cut into the underside
        L(-HullHalf, 0, -PortHalf, 0);
        L(-PortHalf, 0, -PortHalf, 16);
        L(-PortHalf, 16, PortHalf, 16);
        L(PortHalf, 16, PortHalf, 0);
        L(PortHalf, 0, HullHalf, 0);
        L(HullHalf, 0, HullHalf - 22, 40);
        L(HullHalf - 22, 40, -HullHalf + 22, 40);
        L(-HullHalf + 22, 40, -HullHalf, 0);

        // superstructure and antenna
        L(-52, 40, -40, HullHeight);
        L(-40, HullHeight, 40, HullHeight);
        L(40, HullHeight, 52, 40);
        L(0, HullHeight, 0, HullHeight + 24, 1.2f);

        // windows
        for (int k = 0; k < 4; k++)
        {
            float wx = 44 + k * 15;
            L(wx, 14, wx, 26, 1.2f);
            L(-wx, 14, -wx, 26, 1.2f);
        }

        // side-thruster puffs while the mothership is being steered (the jet is on the trailing end)
        if (_msSteer != 0)
        {
            float end = -_msSteer * HullHalf;
            for (int k = 0; k < 3; k++)
            {
                float yy = 8 + k * 11;
                float len = 12f + Rand() * 16f;
                Line(P(end, yy), P(end - _msSteer * len, yy), Amber, 1.4f);
            }
        }

        // blinking lights: antenna and docking-bay markers (the bay lights are what you steer towards)
        bool on = ((int)(_time * 2.5f) & 1) == 0;
        if (on)
        {
            Gfx.Circle(P(0, HullHeight + 26), 2.5f, Red);
            Gfx.Circle(P(-PortHalf, -5), 2.5f, Green);
            Gfx.Circle(P(PortHalf, -5), 2.5f, Green);
        }
    }

    static void Line(Vector2 a, Vector2 b, Color c, float w = 1.6f) => Gfx.Line(a, b, w, c);

    /// <summary>Faint "+" markers pinned to the sky: the only cue for your own motion when no ground is in view.</summary>
    void DrawMarkers()
    {
        int x0 = (int)MathF.Floor((_x - _c.X - 20f) / Grid);
        int x1 = (int)MathF.Ceiling((_x + _c.X + 20f) / Grid);
        int y0 = (int)MathF.Floor((_y - _c.Y - 20f) / Grid);
        int y1 = (int)MathF.Ceiling((_y + _c.Y + 20f) / Grid);
        for (int gx = x0; gx <= x1; gx++)
        {
            for (int gy = y0; gy <= y1; gy++)
            {
                float wx = gx * Grid, wy = gy * Grid;
                if (wy < Height(wx) + 20f)
                    continue; // below the surface
                Vector2 p = ToScreen(wx, wy);
                Line(p - new Vector2(5, 0), p + new Vector2(5, 0), Dim, 1f);
                Line(p - new Vector2(0, 5), p + new Vector2(0, 5), Dim, 1f);
            }
        }
    }

    void DrawTerrain()
    {
        float reach = _c.X + Step * 2;
        for (int i = 0; i < N; i++)
        {
            float rel = DeltaX(i * Step - _x);
            if (MathF.Abs(rel) > reach)
                continue;

            int j = (i + 1) % N;
            Vector2 a = new(_c.X + rel, _c.Y - (_h[i] - _y));
            Vector2 b = new(_c.X + rel + Step, _c.Y - (_h[j] - _y));
            if ((a.Y < -40 && b.Y < -40) || (a.Y > Gfx.Height + 40 && b.Y > Gfx.Height + 40))
                continue;

            bool isPad = _pads.Any(p => i >= p.Start && i < p.Start + p.Count - 1);
            Line(a, b, isPad ? Amber : Rock, isPad ? 3f : 1.6f);
        }

        foreach (var pad in _pads)
        {
            float mid = (pad.X0 + pad.X1) / 2f;
            Vector2 p = ToScreen(mid, _h[pad.Start]);
            if (p.X < -40 || p.X > Gfx.Width + 40 || p.Y < -40 || p.Y > Gfx.Height + 40)
                continue;
            string label = $"x{pad.Multiplier}";
            Gfx.Text(label, (int)p.X - Gfx.Measure(label, 16) / 2, (int)p.Y + 8, 16, Amber);
        }
    }

    void DrawPieces()
    {
        foreach (var p in _pieces)
        {
            Vector2 half = new Vector2(MathF.Cos(p.Angle), MathF.Sin(p.Angle)) * (p.Length / 2);
            Vector2 a = ToScreen(p.Pos.X - half.X, p.Pos.Y - half.Y);
            Vector2 b = ToScreen(p.Pos.X + half.X, p.Pos.Y + half.Y);
            Line(a, b, Gfx.Fade(Green, p.Life / p.MaxLife));
        }
    }

    /// <summary>The lander never moves or rotates on screen. (Screen space: y is down.)</summary>
    void DrawLander()
    {
        Vector2 c = _c;
        Vector2 P(float x, float y) => c + new Vector2(x, y);

        // cabin (an octagon)
        Vector2[] cabin =
        {
            P(-6, -18), P(6, -18), P(12, -10), P(12, -2), P(6, 4), P(-6, 4), P(-12, -2), P(-12, -10),
        };
        for (int i = 0; i < cabin.Length; i++)
            Line(cabin[i], cabin[(i + 1) % cabin.Length], Green);

        // window
        Line(P(-5, -13), P(5, -13), Green, 1.2f);
        Line(P(5, -13), P(5, -8), Green, 1.2f);
        Line(P(5, -8), P(-5, -8), Green, 1.2f);
        Line(P(-5, -8), P(-5, -13), Green, 1.2f);

        // descent engine bell
        Line(P(-5, 4), P(-8, 10), Green);
        Line(P(5, 4), P(8, 10), Green);
        Line(P(-8, 10), P(8, 10), Green);

        // legs and foot pads
        Line(P(-10, 2), P(-FootDx, FootDy), Green);
        Line(P(10, 2), P(FootDx, FootDy), Green);
        Line(P(-FootDx - 4, FootDy), P(-FootDx + 4, FootDy), Green);
        Line(P(FootDx - 4, FootDy), P(FootDx + 4, FootDy), Green);

        // exhaust
        if (_state == State.Flying && _boost)
        {
            float flame = 40f * (0.75f + Rand() * 0.5f);
            Line(P(-6, 11), P(0, 11 + flame), Amber);
            Line(P(6, 11), P(0, 11 + flame), Amber);
        }
        if (_state == State.Flying)
        {
            float puff = 6f + Rand() * 8f;
            if (_sideLeft) Line(P(12, -8), P(12 + puff, -8), Amber); // jet on the right pushes us left
            if (_sideRight) Line(P(-12, -8), P(-12 - puff, -8), Amber);
        }
    }

    void DrawHud()
    {
        int w = Gfx.Width, h = Gfx.Height;

        // left column
        Gfx.Text($"SCORE  {_score:D5}", 20, 14, 22, Green);
        Gfx.Text($"ROUND  {_roundNo}  {(_round == Round.Descent ? "DESCENT" : "ASCENT")}", 20, 42, 18, Dim);
        Gfx.Text($"SHIPS  {Math.Max(_lives, 0)}", 20, 64, 18, Dim);

        Color fuelColor = _fuel < 150 ? Red : Green;
        Gfx.Text($"FUEL   {(int)MathF.Max(_fuel, 0)}", 20, 96, 18, fuelColor);
        Gfx.Rect(20, 120, 160, 10, Dim);
        Gfx.FillRect(21, 121, 158 * Math.Clamp(_fuel / MaxFuel, 0f, 1f), 8, fuelColor);

        // right column (classic lander read-outs)
        float vx = _vel.X;
        float vy = _vel.Y;
        int rx = w - 270;
        string Arrow(float v, string neg, string pos) => v < -0.5f ? neg : v > 0.5f ? pos : " ";
        Gfx.Text($"ALTITUDE          {(int)MathF.Max(Altitude(), 0),5}", rx, 14, 18, Green);
        if (_round == Round.Descent)
        {
            bool xOk = MathF.Abs(vx) <= MaxLandingVx;
            bool yOk = -vy <= MaxLandingVy;
            Gfx.Text($"HORIZONTAL SPEED  {(int)MathF.Abs(vx),5} {Arrow(vx, "<", ">")}", rx, 38, 18, xOk ? Green : Amber);
            Gfx.Text($"VERTICAL SPEED    {(int)MathF.Abs(vy),5} {Arrow(vy, "v", "^")}", rx, 62, 18, yOk ? Green : Amber);
        }
        else
        {
            // Docking read-outs: speed is measured relative to the (moving) mothership.
            float rvx = vx - _msVx;
            bool xOk = MathF.Abs(rvx) <= MaxDockRelVx;
            bool yOk = vy <= MaxDockVy;
            float gap = _msY - (_y + LanderTop);
            Gfx.Text($"DOCK HEIGHT       {(int)MathF.Max(gap, 0),5}", rx, 38, 18, Cyan);
            Gfx.Text($"REL H SPEED       {(int)MathF.Abs(rvx),5} {Arrow(rvx, "<", ">")}", rx, 62, 18, xOk ? Green : Amber);
            Gfx.Text($"VERTICAL SPEED    {(int)MathF.Abs(vy),5} {Arrow(vy, "v", "^")}", rx, 86, 18, yOk ? Green : Amber);
        }

        DrawPadRadar(w, h);

        string hint = _state == State.Undocking
            ? "LEFT/RIGHT slide the mothership over a landing pad   ENTER or SPACE release the lander"
            : _round == Round.Descent
                ? "Hold UP (or SPACE) to boost   LEFT/RIGHT side boosters   land on a pad   P pause"
                : "Hold UP to take off   LEFT/RIGHT side boosters   match the mothership's speed and enter its bay   P pause";
        Gfx.Text(hint, 20, h - 28, 16, Dim);

        // Mission banner for the first few seconds of each round
        if (_state == State.Flying && (_onGround || _flightTime < 4f))
        {
            if (_round == Round.Descent)
                Centered("DESCEND - land on a pad", 70, 24, Green);
            else
                Centered("ASCEND - take off and dock with the mothership", 70, 24, Cyan);
        }

        switch (_state)
        {
            case State.Undocking:
                // (kept near the top of the screen: the mothership's hull sits just above the lander)
                Centered("UNDOCKING", 110, 36, Cyan);
                Centered("Steer the mothership over a pad with LEFT/RIGHT, then press ENTER to release", 156, 20, Amber);
                break;
            case State.Landed:
                Centered("THE LANDER HAS LANDED", h / 2 - 130, 36, Green);
                Centered($"+{_lastAward} points  (x{_lastMult} pad, fuel bonus)", h / 2 - 84, 22, Amber);
                Centered("Next: take off and dock with the mothership.  Press ENTER", h / 2 - 54, 20, Dim);
                break;
            case State.Docked:
                Centered("DOCKED", 110, 40, Cyan);
                Centered($"+{_lastAward} points  (refuelled)", 160, 22, Amber);
                Centered("Press ENTER to undock for the next descent", 192, 20, Dim);
                break;
            case State.Crashed:
                Centered("CRASHED", h / 2 - 130, 40, Red);
                Centered($"{_lives} ship{(_lives == 1 ? "" : "s")} remaining", h / 2 - 84, 22, Amber);
                Centered("Press ENTER to try again", h / 2 - 54, 20, Dim);
                break;
            case State.GameOver:
                Centered("GAME OVER", h / 2 - 130, 44, Red);
                Centered($"Final score {_score}", h / 2 - 80, 22, Amber);
                Centered("Press ENTER to play again", h / 2 - 50, 20, Dim);
                break;
        }

        if (_paused)
            Centered("PAUSED", h / 2 - 130, 40, Green);
    }

    /// <summary>
    /// Strip along the bottom of the screen showing the whole moon laid out flat, with the lander at the
    /// centre. Each landing pad is a marker with its multiplier above and its horizontal distance
    /// (with a &lt; or &gt; for the direction) below. The nearest pad is drawn brightest, and the box shows
    /// how much of the moon is currently on screen.
    /// </summary>
    void DrawPadRadar(int w, int h)
    {
        const int labelSize = 14;
        float half = w / 2f - 70f;
        float y = h - 74f;
        float cx = _c.X;
        float scale = half / (Length / 2f); // strip pixels per world unit

        // translucent backing so terrain passing behind the strip doesn't clutter the labels
        Gfx.FillRect(0, y - 34f, w, h - (y - 34f), Color.FromArgb(200, 0, 0, 0));

        // baseline, end caps and the "what's on screen" window
        Line(new Vector2(cx - half, y), new Vector2(cx + half, y), Dim, 1f);
        Line(new Vector2(cx - half, y - 4), new Vector2(cx - half, y + 4), Dim, 1f);
        Line(new Vector2(cx + half, y - 4), new Vector2(cx + half, y + 4), Dim, 1f);
        float view = _c.X * scale;
        Gfx.Rect(cx - view, y - 7, view * 2, 14, Dim);

        // Descent rounds are about the pads (nearest one brightest); ascent rounds are about the mothership.
        bool ascent = _round == Round.Ascent;
        Pad? nearest = null;
        float best = float.MaxValue;
        foreach (var pad in _pads)
        {
            float d = MathF.Abs(DeltaX((pad.X0 + pad.X1) / 2f - _x));
            if (d < best)
            {
                best = d;
                nearest = pad;
            }
        }

        // mothership marker: a hollow hull-sized box (it is moving, so this one drifts along the strip)
        {
            float rel = DeltaX(_msX - _x);
            float px = cx + rel * scale;
            float bw = MathF.Max(10f, HullHalf * 2f * scale);
            Color col = ascent ? Cyan : Gfx.Fade(Cyan, 0.55f);
            Gfx.Rect(px - bw / 2f, y - 5f, bw, 10f, col);
            Gfx.FillRect(px - 2f, y - 2f, 4f, 4f, col);
            Gfx.Text("MS", (int)px - Gfx.Measure("MS", labelSize) / 2, (int)y - 26, labelSize, col);
            float dist = MathF.Abs(rel);
            string text = dist < 12f ? "HERE" : rel < 0 ? $"<{(int)dist}" : $"{(int)dist}>";
            Gfx.Text(text, (int)px - Gfx.Measure(text, labelSize) / 2, (int)y + 9, labelSize, col);
        }

        foreach (var pad in _pads)
        {
            float rel = DeltaX((pad.X0 + pad.X1) / 2f - _x);
            float px = cx + rel * scale;
            float markerW = MathF.Max(5f, (pad.X1 - pad.X0) * scale);
            Color col = pad == nearest && !ascent ? Amber : Gfx.Fade(Amber, 0.55f);

            Gfx.FillRect(px - markerW / 2f, y - 3f, markerW, 6f, col);

            string mult = $"x{pad.Multiplier}";
            Gfx.Text(mult, (int)px - Gfx.Measure(mult, labelSize) / 2, (int)y - 26, labelSize, col);

            float dist = MathF.Abs(rel);
            string dist_s = dist < 12f ? "HERE" : rel < 0 ? $"<{(int)dist}" : $"{(int)dist}>";
            Gfx.Text(dist_s, (int)px - Gfx.Measure(dist_s, labelSize) / 2, (int)y + 9, labelSize, col);
        }

        // lander marker, drawn last so it sits on top
        Vector2 s = new(cx, y);
        Line(s + new Vector2(0, -8), s + new Vector2(-5, 3), Green, 1.4f);
        Line(s + new Vector2(-5, 3), s + new Vector2(5, 3), Green, 1.4f);
        Line(s + new Vector2(5, 3), s + new Vector2(0, -8), Green, 1.4f);
    }

    static void Centered(string text, int y, int size, Color color) =>
        Gfx.Text(text, (Gfx.Width - Gfx.Measure(text, size)) / 2, y, size, color);
}
