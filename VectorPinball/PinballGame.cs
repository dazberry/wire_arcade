using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using WireGames;

namespace VectorPinball;

/// <summary>
/// Vector Pinball: a whole table drawn with nothing but lines. Table coordinates are 600 x 1000 units
/// (y down); the table is scaled to fit the window. The physics runs in small sub-steps so the ball
/// never tunnels through a fast flipper.
/// </summary>
public sealed class PinballGame : IGame
{
    // ---- table --------------------------------------------------------------------------------
    const float TableW = 600f, TableH = 1000f;
    const float BallR = 10f;
    const float Gravity = 700f;
    const float MaxSpeed = 1800f;
    const int SubSteps = 10;
    const float DrainY = 1020f;

    const float LaneX = 560f;          // centre of the plunger lane
    const float LaneRestY = 938f;
    const float LaneWallX = 540f;

    // ---- flippers ----------------------------------------------------------------------------
    const float FlipLen = 92f;
    const float FlipRest = 0.55f, FlipUp = -0.5f;
    const float FlipUpSpeed = 22f, FlipDownSpeed = 12f;

    const int BallsPerGame = 3;

    static readonly Color Green = Color.FromArgb(255, 120, 255, 150);
    static readonly Color Amber = Color.FromArgb(255, 255, 190, 80);
    static readonly Color Red = Color.FromArgb(255, 255, 90, 80);
    static readonly Color Rock = Color.FromArgb(255, 225, 235, 255);
    static readonly Color Dim = Color.FromArgb(255, 70, 90, 80);
    static readonly Color Cyan = Color.FromArgb(255, 120, 215, 255);

    sealed class Seg
    {
        public Vector2 A, B;
        public float Restitution = 0.5f;
        public float Kick;          // extra speed along the normal (slingshots)
        public int Score;
        public int Target = -1;     // index into _targetLit, or -1
        public float Flash;
        public Seg(Vector2 a, Vector2 b) { A = a; B = b; }
    }

    sealed class Round
    {
        public Vector2 C;
        public float R, Restitution = 0.6f, Kick;
        public int Score;
        public float Flash;
    }

    sealed class Flipper
    {
        public Vector2 Pivot;
        public float Dir;           // +1: tip points right (left flipper), -1: mirrored
        public float Angle = FlipRest, Prev = FlipRest;
        public bool Active;

        public Vector2 Pt(float s, float ang) => Pivot + new Vector2(Dir * MathF.Cos(ang), MathF.Sin(ang)) * s;
        public static float Thick(float s) => 13f - 6f * s / FlipLen;
    }

    enum State { Title, Playing, BallLost, GameOver }

    readonly List<Seg> _segs = new();
    readonly List<Round> _rounds = new();
    readonly Flipper _left = new() { Pivot = new Vector2(170, 880), Dir = 1 };
    readonly Flipper _right = new() { Pivot = new Vector2(390, 880), Dir = -1 };
    readonly bool[] _targetLit = new bool[4];
    readonly Queue<Vector2> _trail = new();

    State _state = State.Title;
    Vector2 _p, _v;
    float _spin;
    float _charge;
    float _time;
    float _stateTime;
    float _slingLock;       // slingshots can't kick again until this reaches 0, so the ball can't ping-pong between them
    long _score, _high;
    int _ball = 1, _mult = 1;
    string _message = "";

    public PinballGame()
    {
        BuildTable();
        ResetBall();
    }

    // ===========================================================================================
    //  Table construction
    // ===========================================================================================
    void BuildTable()
    {
        // arch across the top (a half circle), then the side walls
        Vector2 centre = new(300, 300);
        const float archR = 280f;
        const int n = 28;
        Vector2 prev = new(20, 300);
        for (int i = 1; i <= n; i++)
        {
            float a = MathF.PI + MathF.PI * i / n;
            Vector2 p = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * archR;
            AddWall(prev, p);
            prev = p;
        }
        AddWall(new Vector2(20, 300), new Vector2(20, 770));
        AddWall(new Vector2(580, 300), new Vector2(580, 950));
        AddWall(new Vector2(LaneWallX, 380), new Vector2(LaneWallX, 950));
        AddWall(new Vector2(LaneWallX, 950), new Vector2(580, 950));

        // slopes feeding the flippers
        // (they end on the flippers' upper edge at rest, so the ball rolls on without catching on a step)
        AddWall(new Vector2(20, 770), SlopeEnd(_left));
        AddWall(new Vector2(LaneWallX, 770), SlopeEnd(_right));

        // slingshots above the flippers (mirrored about the middle of the playfield, x = 280)
        AddSling(new Vector2(70, 640), new Vector2(70, 730), new Vector2(140, 785));
        AddSling(M(new Vector2(70, 640)), M(new Vector2(70, 730)), M(new Vector2(140, 785)));

        // stand-up targets along the side walls
        AddTarget(new Vector2(31, 430), new Vector2(31, 490), 0);
        AddTarget(new Vector2(31, 520), new Vector2(31, 580), 1);
        AddTarget(new Vector2(529, 430), new Vector2(529, 490), 2);
        AddTarget(new Vector2(529, 520), new Vector2(529, 580), 3);

        // pop bumpers and pegs
        AddBumper(new Vector2(180, 260), 30);
        AddBumper(new Vector2(380, 260), 30);
        AddBumper(new Vector2(280, 390), 30);
        foreach (var peg in new[]
        {
            new Vector2(110, 190), new Vector2(450, 190), new Vector2(280, 150),
            new Vector2(100, 520), new Vector2(460, 520), new Vector2(190, 560), new Vector2(370, 560),
        })
            _rounds.Add(new Round { C = peg, R = 9, Score = 10, Restitution = 0.7f });
    }

    static Vector2 SlopeEnd(Flipper f) =>
        f.Pivot + new Vector2(f.Dir * MathF.Sin(FlipRest), -MathF.Cos(FlipRest)) * Flipper.Thick(0);

    static Vector2 M(Vector2 p) => new(560f - p.X, p.Y);

    void AddWall(Vector2 a, Vector2 b) => _segs.Add(new Seg(a, b));

    void AddTarget(Vector2 a, Vector2 b, int idx) =>
        _segs.Add(new Seg(a, b) { Target = idx, Score = 500, Restitution = 0.4f });

    void AddSling(Vector2 a, Vector2 b, Vector2 c)
    {
        // a-b is the back, b-c the bottom, a-c the kicking face
        _segs.Add(new Seg(a, b));
        _segs.Add(new Seg(b, c));
        _segs.Add(new Seg(a, c) { Kick = 260f, Score = 10, Restitution = 0.5f });
    }

    void AddBumper(Vector2 c, float r) =>
        _rounds.Add(new Round { C = c, R = r, Kick = 420f, Score = 100, Restitution = 0.4f });

    // ===========================================================================================
    //  Game flow
    // ===========================================================================================
    void ResetBall()
    {
        _p = new Vector2(LaneX, LaneRestY);
        _v = Vector2.Zero;
        _charge = 0;
        _trail.Clear();
    }

    void StartGame()
    {
        _score = 0;
        _ball = 1;
        _mult = 1;
        Array.Clear(_targetLit);
        ResetBall();
        SetState(State.Playing);
        _message = "";
    }

    void SetState(State s) { _state = s; _stateTime = 0; }

    void Award(int pts)
    {
        _score += (long)pts * _mult;
        if (_score > _high) _high = _score;
    }

    public void Update(float dt)
    {
        _time += dt;
        _stateTime += dt;

        switch (_state)
        {
            case State.Title:
                if (Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space)) StartGame();
                return;

            case State.GameOver:
                if (_stateTime > 1f && (Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space))) StartGame();
                return;

            case State.BallLost:
                if (_stateTime > 2f)
                {
                    if (_ball >= BallsPerGame) { SetState(State.GameOver); _message = ""; }
                    else { _ball++; ResetBall(); SetState(State.Playing); _message = ""; }
                }
                return;
        }

        _left.Active = Input.Down(Keys.Left) || Input.Down(Keys.Z) || Input.Down(Keys.A);
        _right.Active = Input.Down(Keys.Right) || Input.Down(Keys.OemQuestion) || Input.Down(Keys.D) || Input.Down(Keys.M);

        bool inLane = _p.X > LaneWallX && _p.Y > 880;
        bool plunger = Input.Down(Keys.Space) || Input.Down(Keys.Down);
        if (inLane && plunger)
            _charge = MathF.Min(1f, _charge + dt / 1.1f);
        else if (_charge > 0)
        {
            if (inLane)
                _v = new Vector2(0, -(1080f + 820f * _charge));
            _charge = 0;
        }

        float h = Math.Min(dt, 0.033f) / SubSteps;
        for (int i = 0; i < SubSteps; i++)
            Step(h);

        _slingLock = MathF.Max(0, _slingLock - dt);
        foreach (var s in _segs) s.Flash = MathF.Max(0, s.Flash - dt);
        foreach (var r in _rounds) r.Flash = MathF.Max(0, r.Flash - dt);

        _spin += _v.Length() * dt * 0.05f;
        _trail.Enqueue(_p);
        while (_trail.Count > 9) _trail.Dequeue();

        if (_p.Y > DrainY)
        {
            _message = "BALL LOST";
            _mult = 1;
            SetState(State.BallLost);
        }
    }

    // ===========================================================================================
    //  Physics
    // ===========================================================================================
    void Step(float h)
    {
        Swing(_left, h);
        Swing(_right, h);

        _v.Y += Gravity * h;
        _p += _v * h;

        foreach (var s in _segs) CollideSeg(s);
        foreach (var r in _rounds) CollideRound(r);
        CollideFlipper(_left, h);
        CollideFlipper(_right, h);

        float sp = _v.Length();
        if (sp > MaxSpeed) _v *= MaxSpeed / sp;
    }

    static void Swing(Flipper f, float h)
    {
        f.Prev = f.Angle;
        float target = f.Active ? FlipUp : FlipRest;
        float speed = f.Active ? FlipUpSpeed : FlipDownSpeed;
        float diff = target - f.Angle;
        f.Angle += Math.Clamp(diff, -speed * h, speed * h);
    }

    static Vector2 Closest(Vector2 a, Vector2 b, Vector2 p, out float t)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        t = len2 < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return a + ab * t;
    }

    /// <summary>Push the ball out of a surface and reflect its velocity relative to the surface's own velocity.</summary>
    bool Bounce(Vector2 contact, float radius, Vector2 surfaceVel, float restitution, float kick)
    {
        Vector2 d = _p - contact;
        float dist = d.Length();
        if (dist >= BallR + radius) return false;

        Vector2 normal = dist > 1e-4f ? d / dist : -Vector2.UnitY;
        _p = contact + normal * (BallR + radius);

        Vector2 rel = _v - surfaceVel;
        float vn = Vector2.Dot(rel, normal);
        if (vn < 0)
        {
            rel -= (1f + restitution) * vn * normal;
            _v = rel + surfaceVel;
        }
        bool real = vn < -30f;
        if (kick > 0 && real)
            _v += normal * kick;
        return real;
    }

    void CollideSeg(Seg s)
    {
        Vector2 q = Closest(s.A, s.B, _p, out _);
        bool kicks = s.Kick > 0 && _slingLock <= 0;
        if (!Bounce(q, 0, Vector2.Zero, s.Restitution, kicks ? s.Kick : 0) || s.Score <= 0) return;
        if (kicks) _slingLock = 1.2f;

        s.Flash = 0.25f;
        Award(s.Score);
        if (s.Target >= 0 && !_targetLit[s.Target])
        {
            _targetLit[s.Target] = true;
            if (_targetLit.All(x => x))
            {
                Award(5000);
                _mult = Math.Min(5, _mult + 1);
                _message = $"BONUS 5000   x{_mult}";
                _stateTime = 0;
                Array.Clear(_targetLit);
            }
        }
    }

    void CollideRound(Round r)
    {
        if (!Bounce(r.C, r.R, Vector2.Zero, r.Restitution, r.Kick)) return;
        r.Flash = 0.2f;
        Award(r.Score);
    }

    void CollideFlipper(Flipper f, float h)
    {
        Vector2 a = f.Pt(0, f.Angle), b = f.Pt(FlipLen, f.Angle);
        Vector2 q = Closest(a, b, _p, out float t);
        float s = t * FlipLen;
        Vector2 surf = (f.Pt(s, f.Angle) - f.Pt(s, f.Prev)) / h;
        Bounce(q, Flipper.Thick(s), surf, 0.35f, 0);
    }

    // ===========================================================================================
    //  Drawing
    // ===========================================================================================
    float _scale;
    Vector2 _origin;

    Vector2 T(Vector2 p) => _origin + p * _scale;

    void Line(Vector2 a, Vector2 b, float w, Color c) => Gfx.Line(T(a), T(b), Math.Max(1f, w * _scale), c);

    void Ring(Vector2 c, float r, float w, Color col, int n = 24)
    {
        Vector2 prev = c + new Vector2(r, 0);
        for (int i = 1; i <= n; i++)
        {
            float a = MathF.Tau * i / n;
            Vector2 p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            Line(prev, p, w, col);
            prev = p;
        }
    }

    public void Draw()
    {
        int w = Gfx.Width, h = Gfx.Height;
        _scale = Math.Min((h - 30f) / TableH, (w - 40f) / TableW);
        _origin = new Vector2((w - TableW * _scale) / 2f, (h - TableH * _scale) / 2f);

        DrawBackdrop(w, h);
        DrawTable();
        DrawHud(w, h);

        if (_state == State.Title)
        {
            Panel(w, h, "VECTOR PINBALL", "press SPACE or ENTER to play",
                "Z / LEFT / A  left flipper     M / RIGHT / D  right flipper",
                "hold SPACE or DOWN to pull the plunger, release to launch");
        }
        else if (_state == State.GameOver)
        {
            Panel(w, h, "GAME OVER", _score >= _high && _score > 0 ? "NEW HIGH SCORE!" : $"score {_score:N0}",
                _stateTime > 1f ? "press SPACE or ENTER to play again" : "", "");
        }
        else if (_message.Length > 0 && (_state == State.BallLost || _stateTime < 2.5f))
        {
            Centered(_message, (int)(h * 0.5f), 34, _state == State.BallLost ? Red : Amber);
        }
    }

    static void Centered(string text, int y, int size, Color color) =>
        Gfx.Text(text, (Gfx.Width - Gfx.Measure(text, size)) / 2, y, size, color);

    static void Panel(int w, int h, string title, string line1, string line2, string line3)
    {
        float bw = Math.Min(w - 40f, 700f);
        float bh = 220f;
        float x = (w - bw) / 2f, y = (h - bh) / 2f;
        Gfx.FillRect(x, y, bw, bh, Color.FromArgb(230, 0, 0, 0));
        Gfx.Rect(x, y, bw, bh, Green);
        Centered(title, (int)y + 22, 48, Green);
        Centered(line1, (int)y + 94, 22, Amber);
        Centered(line2, (int)y + 140, 16, Rock);
        Centered(line3, (int)y + 164, 16, Rock);
    }

    void DrawBackdrop(int w, int h)
    {
        const float grid = 110f;
        float ox = _time * 8f % grid, oy = _time * 4f % grid;
        for (float x = -ox; x < w + grid; x += grid)
            for (float y = -oy; y < h + grid; y += grid)
            {
                Gfx.Line(new Vector2(x - 3, y), new Vector2(x + 3, y), 1f, Gfx.Fade(Dim, 0.45f));
                Gfx.Line(new Vector2(x, y - 3), new Vector2(x, y + 3), 1f, Gfx.Fade(Dim, 0.45f));
            }
    }

    void DrawTable()
    {
        // walls, targets and slings
        foreach (var s in _segs)
        {
            if (s.Target >= 0)
                Line(s.A, s.B, 6f, _targetLit[s.Target] || s.Flash > 0 ? Amber : Cyan);
            else
                Line(s.A, s.B, 3f, s.Flash > 0 ? Amber : Green);
        }

        // bumpers and pegs
        foreach (var r in _rounds)
        {
            bool hit = r.Flash > 0;
            Color c = hit ? Amber : (r.Kick > 0 ? Red : Rock);
            Ring(r.C, r.R, 2.5f, c, r.R > 15 ? 28 : 14);
            if (r.Kick > 0)
            {
                Ring(r.C, r.R * 0.55f, 2f, hit ? Amber : Dim, 18);
                if (hit) Ring(r.C, r.R + 8f, 1.5f, Gfx.Fade(Amber, 0.5f), 28);
            }
        }

        DrawFlipper(_left);
        DrawFlipper(_right);

        // plunger: a spring that compresses as it's pulled back
        float pull = _charge * 30f;
        Line(new Vector2(LaneX - 14, 958 + pull), new Vector2(LaneX + 14, 958 + pull), 4f, Amber);
        Vector2 sp = new(LaneX, 958 + pull);
        for (int i = 1; i <= 4; i++)
        {
            Vector2 np = new(LaneX + (i % 2 == 0 ? 8 : -8), 958 + pull + (32f - pull) * i / 4f);
            Line(sp, np, 1.5f, Dim);
            sp = np;
        }

        // ball: fading trail, ring and a spinning spoke
        int k = 0;
        foreach (var t in _trail)
        {
            k++;
            Ring(t, BallR * (0.4f + 0.5f * k / _trail.Count), 1f, Gfx.Fade(Cyan, 0.35f * k / (_trail.Count + 1)), 10);
        }
        Ring(_p, BallR, 2.5f, Rock, 16);
        Vector2 sd = new Vector2(MathF.Cos(_spin), MathF.Sin(_spin)) * BallR;
        Line(_p - sd, _p + sd, 1.5f, Rock);
    }

    void DrawFlipper(Flipper f)
    {
        Vector2 a = f.Pt(0, f.Angle), b = f.Pt(FlipLen, f.Angle);
        float ra = Flipper.Thick(0), rb = Flipper.Thick(FlipLen);
        Vector2 dir = Vector2.Normalize(b - a);
        Vector2 nrm = new(-dir.Y, dir.X);
        Color c = f.Active ? Amber : Green;
        Line(a + nrm * ra, b + nrm * rb, 3f, c);
        Line(a - nrm * ra, b - nrm * rb, 3f, c);
        Ring(a, ra, 3f, c, 16);
        Ring(b, rb, 3f, c, 12);
        Ring(a, 3f, 1.5f, c, 8);
    }

    void DrawHud(int w, int h)
    {
        int size = Math.Clamp((int)(h * 0.05f), 18, 40);
        int small = Math.Clamp((int)(h * 0.026f), 14, 20);

        float lx = Math.Max(10f, _origin.X - 260f);
        Gfx.Text("SCORE", (int)lx, 40, small, Dim);
        Gfx.Text($"{_score:N0}", (int)lx, 40 + small + 4, size, Green);
        Gfx.Text("HIGH", (int)lx, 40 + small + size + 28, small, Dim);
        Gfx.Text($"{_high:N0}", (int)lx, 40 + 2 * small + size + 32, size, Amber);

        float rx = _origin.X + TableW * _scale + 20f;
        Gfx.Text("BALL", (int)rx, 40, small, Dim);
        Gfx.Text($"{Math.Min(_ball, BallsPerGame)} / {BallsPerGame}", (int)rx, 40 + small + 4, size, Green);
        Gfx.Text("MULTIPLIER", (int)rx, 40 + small + size + 28, small, Dim);
        Gfx.Text($"x{_mult}", (int)rx, 40 + 2 * small + size + 32, size, Amber);

        Gfx.Text("TARGETS", (int)rx, 40 + 2 * small + 2 * size + 70, small, Dim);
        for (int i = 0; i < 4; i++)
        {
            var c = new Vector2(rx + i * 26 + 8, 40 + 3 * small + 2 * size + 86);
            if (_targetLit[i]) Gfx.Circle(c, 7, Amber);
            else Gfx.CircleOutline(c, 7, Dim);
        }

        Gfx.Text("ESC  menu", 10, h - 28, 14, Dim);
    }
}
