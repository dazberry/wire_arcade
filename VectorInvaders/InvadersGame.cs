using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using WireGames;

namespace VectorInvaders;

/// <summary>
/// Vector Invaders: the classic fixed-shooter, but every sprite, bunker and explosion is drawn as an
/// outline. Sprites are tiny bitmaps whose edges are traced into line segments, so the shields can be
/// eaten away bit by bit and still look like wireframe.
/// Field coordinates are 640 x 720 units (y down), scaled to fit the window.
/// </summary>
public sealed class InvadersGame : IGame
{
    // ---- field ---------------------------------------------------------------------------------
    const float FieldW = 640f, FieldH = 720f;
    const float Cell = 3f;             // size of one sprite "pixel"

    // ---- invaders ------------------------------------------------------------------------------
    const int Cols = 11, Rows = 5;
    const float SpacingX = 48f, SpacingY = 40f;
    const float StepX = 8f, StepDrop = 20f;
    const float EdgeMargin = 22f;
    const float InvaderLimitY = 640f;  // reaching this ends the game

    // ---- player --------------------------------------------------------------------------------
    const float PlayerY = 676f;
    const float PlayerSpeed = 270f;
    const float BulletSpeed = 540f;
    const float BombSpeed = 210f;
    const int StartLives = 3;

    // ---- shields -------------------------------------------------------------------------------
    const int ShieldW = 22, ShieldH = 16;
    const float ShieldCell = 4f;
    const float ShieldY = 560f;

    static readonly Color Green = Color.FromArgb(255, 120, 255, 150);
    static readonly Color Amber = Color.FromArgb(255, 255, 190, 80);
    static readonly Color Red = Color.FromArgb(255, 255, 90, 80);
    static readonly Color Rock = Color.FromArgb(255, 225, 235, 255);
    static readonly Color Dim = Color.FromArgb(255, 70, 90, 80);
    static readonly Color Cyan = Color.FromArgb(255, 120, 215, 255);

    // ===========================================================================================
    //  Sprites
    // ===========================================================================================
    sealed class Sprite
    {
        public readonly int W, H;
        public readonly List<(Vector2 a, Vector2 b)> Edges;
        public Sprite(string[] rows)
        {
            H = rows.Length;
            W = rows[0].Length;
            Edges = Outline((x, y) => rows[y][x] == 'X', W, H);
        }
    }

    static readonly Sprite[][] InvaderSprites =
    {
        new[]
        {
            new Sprite(new[] { "...XX...", "..XXXX..", ".XXXXXX.", "XX.XX.XX", "XXXXXXXX", "..X..X..", ".X.XX.X.", "X.X..X.X" }),
            new Sprite(new[] { "...XX...", "..XXXX..", ".XXXXXX.", "XX.XX.XX", "XXXXXXXX", ".X.XX.X.", "X......X", ".X....X." }),
        },
        new[]
        {
            new Sprite(new[] { "..X.....X..", "...X...X...", "..XXXXXXX..", ".XX.XXX.XX.", "XXXXXXXXXXX", "X.XXXXXXX.X", "X.X.....X.X", "...XX.XX..." }),
            new Sprite(new[] { "..X.....X..", "X..X...X..X", "X.XXXXXXX.X", "XXX.XXX.XXX", "XXXXXXXXXXX", ".XXXXXXXXX.", "..X.....X..", ".X.......X." }),
        },
        new[]
        {
            new Sprite(new[] { "....XXXX....", ".XXXXXXXXXX.", "XXXXXXXXXXXX", "XXX..XX..XXX", "XXXXXXXXXXXX", "...XX..XX...", "..XX.XX.XX..", "XX........XX" }),
            new Sprite(new[] { "....XXXX....", ".XXXXXXXXXX.", "XXXXXXXXXXXX", "XXX..XX..XXX", "XXXXXXXXXXXX", "..XXX..XXX..", ".XX..XX..XX.", "..XX....XX.." }),
        },
    };

    static readonly Sprite UfoSprite = new(new[]
    {
        "......XXXXXX......", "....XXXXXXXXXX....", "...XXXXXXXXXXXX...", "..XX.XX.XX.XX.XX..",
        "..XXXXXXXXXXXXXX..", "....XXX....XXX....", ".....X......X.....",
    });

    static readonly Sprite CannonSprite = new(new[]
    {
        "......X......", ".....XXX.....", ".....XXX.....", ".XXXXXXXXXXX.", "XXXXXXXXXXXXX", "XXXXXXXXXXXXX", "XXXXXXXXXXXXX",
    });

    static readonly Color[] RowColors = { Rock, Cyan, Cyan, Green, Green };
    static readonly int[] RowType = { 0, 1, 1, 2, 2 };
    static readonly int[] RowPoints = { 30, 20, 20, 10, 10 };

    /// <summary>Trace the edges of a bitmap into line segments, merging runs so we draw as few lines as possible.</summary>
    static List<(Vector2 a, Vector2 b)> Outline(Func<int, int, bool> filled, int w, int h)
    {
        bool F(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && filled(x, y);
        var edges = new List<(Vector2, Vector2)>();

        // horizontal edges: a filled cell with an empty neighbour above (or below)
        for (int y = 0; y < h; y++)
        {
            for (int side = 0; side < 2; side++)
            {
                int dy = side == 0 ? -1 : 1;
                int run = -1;
                for (int x = 0; x <= w; x++)
                {
                    bool e = x < w && F(x, y) && !F(x, y + dy);
                    if (e && run < 0) run = x;
                    if (!e && run >= 0)
                    {
                        float ey = y + (side == 0 ? 0 : 1);
                        edges.Add((new Vector2(run, ey), new Vector2(x, ey)));
                        run = -1;
                    }
                }
            }
        }
        // vertical edges: empty neighbour to the left (or right)
        for (int x = 0; x < w; x++)
        {
            for (int side = 0; side < 2; side++)
            {
                int dx = side == 0 ? -1 : 1;
                int run = -1;
                for (int y = 0; y <= h; y++)
                {
                    bool e = y < h && F(x, y) && !F(x + dx, y);
                    if (e && run < 0) run = y;
                    if (!e && run >= 0)
                    {
                        float ex = x + (side == 0 ? 0 : 1);
                        edges.Add((new Vector2(ex, run), new Vector2(ex, y)));
                        run = -1;
                    }
                }
            }
        }
        return edges;
    }

    // ===========================================================================================
    //  State
    // ===========================================================================================
    sealed class Shield
    {
        public Vector2 Pos;
        public readonly bool[,] Cells = new bool[ShieldW, ShieldH];
        public List<(Vector2 a, Vector2 b)>? Edges;

        public Shield(Vector2 pos)
        {
            Pos = pos;
            for (int x = 0; x < ShieldW; x++)
                for (int y = 0; y < ShieldH; y++)
                {
                    bool cutCorner = y < 4 && (x < 4 - y || x >= ShieldW - (4 - y));
                    bool notch = y >= 11 && x >= 7 && x < 15 && (y >= 13 || (x >= 8 && x < 14));
                    Cells[x, y] = !cutCorner && !notch;
                }
        }
    }

    struct Bomb { public Vector2 Pos; public float Wobble; }
    struct Burst { public Vector2 Pos; public float Age, Life, Size; public Color Color; }

    enum State { Title, Playing, Dying, WaveClear, GameOver }

    readonly bool[,] _alive = new bool[Rows, Cols];
    readonly List<Shield> _shields = new();
    readonly List<Bomb> _bombs = new();
    readonly List<Burst> _bursts = new();
    readonly Random _rng = new();

    State _state = State.Title;
    float _stateTime, _time;
    long _score, _high;
    int _lives = StartLives, _wave = 1, _aliveCount;
    bool _extraLifeGiven;

    float _fx, _fy, _dir = 1;
    float _stepTimer;
    int _frame;
    float _bombTimer;

    float _playerX = FieldW / 2f;
    bool _bulletLive;
    Vector2 _bullet;

    bool _ufoLive;
    float _ufoX, _ufoDir, _ufoTimer = 15f;
    float _ufoPopTime;
    int _ufoPopScore;
    Vector2 _ufoPopPos;

    public InvadersGame() => NewWave(false);

    void NewGame()
    {
        _score = 0;
        _lives = StartLives;
        _wave = 1;
        _extraLifeGiven = false;
        NewWave(true);
        SetState(State.Playing);
    }

    void NewWave(bool keepNothing)
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _alive[r, c] = true;
        _aliveCount = Rows * Cols;
        _fx = 60f;
        _fy = 110f + Math.Min(_wave - 1, 6) * 18f;
        _dir = 1;
        _stepTimer = 0.5f;
        _frame = 0;
        _bombTimer = 1f;
        _bombs.Clear();
        _bulletLive = false;
        _ufoLive = false;
        _ufoTimer = 12f + (float)_rng.NextDouble() * 10f;

        _shields.Clear();
        for (int i = 0; i < 4; i++)
            _shields.Add(new Shield(new Vector2(FieldW * (i + 0.5f) / 4f - ShieldW * ShieldCell / 2f, ShieldY)));
    }

    void SetState(State s) { _state = s; _stateTime = 0; }

    void AddScore(int pts)
    {
        _score += pts;
        if (_score > _high) _high = _score;
        if (!_extraLifeGiven && _score >= 1500) { _extraLifeGiven = true; _lives++; }
    }

    // ===========================================================================================
    //  Geometry helpers
    // ===========================================================================================
    Vector2 InvaderPos(int r, int c)
    {
        var s = InvaderSprites[RowType[r]][0];
        return new Vector2(_fx + c * SpacingX + (SpacingX - s.W * Cell) / 2f, _fy + r * SpacingY);
    }

    RectangleF InvaderRect(int r, int c)
    {
        var s = InvaderSprites[RowType[r]][0];
        Vector2 p = InvaderPos(r, c);
        return new RectangleF(p.X, p.Y, s.W * Cell, s.H * Cell);
    }

    RectangleF PlayerRect() =>
        new(_playerX - CannonSprite.W * Cell / 2f, PlayerY, CannonSprite.W * Cell, CannonSprite.H * Cell);

    static bool In(RectangleF r, Vector2 p) => r.Contains(p.X, p.Y);

    /// <summary>Left and right extent of the invaders that are still alive.</summary>
    (float left, float right) Extent()
    {
        float l = float.MaxValue, r = float.MinValue;
        for (int row = 0; row < Rows; row++)
            for (int c = 0; c < Cols; c++)
                if (_alive[row, c])
                {
                    var rc = InvaderRect(row, c);
                    l = Math.Min(l, rc.Left);
                    r = Math.Max(r, rc.Right);
                }
        return (l, r);
    }

    /// <summary>Knock a ragged hole in whichever shield contains the point. Returns true if a shield cell was there.</summary>
    bool HitShield(Vector2 p, float radiusCells)
    {
        foreach (var sh in _shields)
        {
            int cx = (int)MathF.Floor((p.X - sh.Pos.X) / ShieldCell);
            int cy = (int)MathF.Floor((p.Y - sh.Pos.Y) / ShieldCell);
            if (cx < 0 || cy < 0 || cx >= ShieldW || cy >= ShieldH || !sh.Cells[cx, cy]) continue;

            int rr = (int)MathF.Ceiling(radiusCells);
            for (int x = cx - rr; x <= cx + rr; x++)
                for (int y = cy - rr; y <= cy + rr; y++)
                {
                    if (x < 0 || y < 0 || x >= ShieldW || y >= ShieldH) continue;
                    float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d <= radiusCells * (0.65f + 0.35f * (float)_rng.NextDouble()))
                        sh.Cells[x, y] = false;
                }
            sh.Edges = null;
            return true;
        }
        return false;
    }

    void Explode(Vector2 pos, Color color, float size = 18f, float life = 0.45f) =>
        _bursts.Add(new Burst { Pos = pos, Life = life, Size = size, Color = color });

    // ===========================================================================================
    //  Update
    // ===========================================================================================
    public void Update(float dt)
    {
        _time += dt;
        _stateTime += dt;

        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var b = _bursts[i];
            b.Age += dt;
            if (b.Age >= b.Life) _bursts.RemoveAt(i); else _bursts[i] = b;
        }
        _ufoPopTime = MathF.Max(0, _ufoPopTime - dt);

        switch (_state)
        {
            case State.Title:
                if (Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space)) NewGame();
                return;

            case State.GameOver:
                if (_stateTime > 1f && (Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space))) NewGame();
                return;

            case State.Dying:
                if (_stateTime > 1.8f)
                {
                    if (_lives <= 0) SetState(State.GameOver);
                    else { _bombs.Clear(); _bulletLive = false; _playerX = FieldW / 2f; SetState(State.Playing); }
                }
                return;

            case State.WaveClear:
                if (_stateTime > 1.6f)
                {
                    _wave++;
                    NewWave(true);
                    SetState(State.Playing);
                }
                return;
        }

        UpdatePlayer(dt);
        UpdateBullet(dt);
        UpdateInvaders(dt);
        UpdateBombs(dt);
        UpdateUfo(dt);

        if (_state == State.Playing && _aliveCount == 0)
            SetState(State.WaveClear);
    }

    void UpdatePlayer(float dt)
    {
        float dx = 0;
        if (Input.Down(Keys.Left) || Input.Down(Keys.A)) dx -= 1;
        if (Input.Down(Keys.Right) || Input.Down(Keys.D)) dx += 1;
        float half = CannonSprite.W * Cell / 2f;
        _playerX = Math.Clamp(_playerX + dx * PlayerSpeed * dt, 14f + half, FieldW - 14f - half);

        if (!_bulletLive && (Input.Pressed(Keys.Space) || Input.Pressed(Keys.Up) || Input.Pressed(Keys.W)))
        {
            _bulletLive = true;
            _bullet = new Vector2(_playerX, PlayerY - 4f);
        }
    }

    void UpdateBullet(float dt)
    {
        if (!_bulletLive) return;

        float remaining = BulletSpeed * dt;
        while (remaining > 0 && _bulletLive)
        {
            float step = Math.Min(remaining, 3f);
            remaining -= step;
            _bullet.Y -= step;

            if (_bullet.Y < 56f)
            {
                _bulletLive = false;
                Explode(new Vector2(_bullet.X, 56f), Green, 8f, 0.25f);
                break;
            }
            if (HitShield(_bullet, 2.2f)) { _bulletLive = false; break; }

            if (_ufoLive && In(UfoRect(), _bullet))
            {
                KillUfo();
                _bulletLive = false;
                break;
            }

            for (int r = 0; r < Rows && _bulletLive; r++)
                for (int c = 0; c < Cols; c++)
                {
                    if (!_alive[r, c] || !In(InvaderRect(r, c), _bullet)) continue;
                    _alive[r, c] = false;
                    _aliveCount--;
                    AddScore(RowPoints[r]);
                    var rc = InvaderRect(r, c);
                    Explode(new Vector2(rc.X + rc.Width / 2, rc.Y + rc.Height / 2), RowColors[r]);
                    _bulletLive = false;
                    break;
                }
        }
    }

    void UpdateInvaders(float dt)
    {
        if (_aliveCount == 0) return;

        float frac = _aliveCount / (float)(Rows * Cols);
        float interval = Math.Max(0.02f, (0.02f + 0.78f * frac) - 0.025f * Math.Min(_wave - 1, 8));

        _stepTimer -= dt;
        if (_stepTimer <= 0)
        {
            _stepTimer += interval;
            _frame ^= 1;

            var (l, r) = Extent();
            if ((_dir > 0 && r + StepX > FieldW - EdgeMargin) || (_dir < 0 && l - StepX < EdgeMargin))
            {
                _dir = -_dir;
                _fy += StepDrop;
            }
            else
            {
                _fx += _dir * StepX;
            }
        }

        // invaders trample the bunkers and the cannon, and reaching the ground ends the game
        for (int row = 0; row < Rows; row++)
            for (int c = 0; c < Cols; c++)
            {
                if (!_alive[row, c]) continue;
                var rc = InvaderRect(row, c);
                if (rc.Bottom >= ShieldY)
                {
                    for (float x = rc.Left; x <= rc.Right; x += ShieldCell)
                        for (float y = rc.Top; y <= rc.Bottom; y += ShieldCell)
                            HitShield(new Vector2(x, y), 1f);
                }
                if (rc.Bottom >= InvaderLimitY && _state == State.Playing)
                {
                    _lives = 0;
                    PlayerHit();
                    return;
                }
            }
    }

    void UpdateBombs(float dt)
    {
        _bombTimer -= dt;
        int maxBombs = Math.Min(3 + _wave / 3, 5);
        if (_bombTimer <= 0 && _bombs.Count < maxBombs && _aliveCount > 0)
        {
            _bombTimer = 0.35f + (float)_rng.NextDouble() * Math.Max(0.3f, 0.9f - 0.05f * _wave);
            // pick a random column that still has invaders; the lowest one in it drops the bomb
            var cols = new List<int>();
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                    if (_alive[r, c]) { cols.Add(c); break; }
            int col = cols[_rng.Next(cols.Count)];
            for (int r = Rows - 1; r >= 0; r--)
                if (_alive[r, col])
                {
                    var rc = InvaderRect(r, col);
                    _bombs.Add(new Bomb { Pos = new Vector2(rc.X + rc.Width / 2f, rc.Bottom) });
                    break;
                }
        }

        RectangleF player = PlayerRect();
        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            float speed = BombSpeed + 12f * Math.Min(_wave - 1, 10);
            float remaining = speed * dt;
            bool gone = false;
            while (remaining > 0 && !gone)
            {
                float step = Math.Min(remaining, 3f);
                remaining -= step;
                b.Pos.Y += step;
                b.Wobble += step * 0.5f;

                if (HitShield(b.Pos + new Vector2(0, 6), 2.4f))
                {
                    Explode(b.Pos, Green, 8f, 0.25f);
                    gone = true;
                }
                else if (b.Pos.Y > PlayerY + 40f)
                {
                    Explode(new Vector2(b.Pos.X, PlayerY + 36f), Dim, 8f, 0.25f);
                    gone = true;
                }
                else if (_state == State.Playing && In(player, b.Pos + new Vector2(0, 6)))
                {
                    gone = true;
                    PlayerHit();
                }
            }
            if (gone) _bombs.RemoveAt(i); else _bombs[i] = b;
        }
    }

    void PlayerHit()
    {
        _lives = Math.Max(0, _lives - 1);
        Explode(new Vector2(_playerX, PlayerY + 10), Amber, 34f, 1.2f);
        Explode(new Vector2(_playerX, PlayerY + 10), Red, 22f, 0.9f);
        _bulletLive = false;
        SetState(State.Dying);
    }

    RectangleF UfoRect() =>
        new(_ufoX - UfoSprite.W * Cell / 2f, 66f, UfoSprite.W * Cell, UfoSprite.H * Cell);

    void KillUfo()
    {
        int[] table = { 50, 100, 150, 300 };
        int pts = table[_rng.Next(table.Length)];
        AddScore(pts);
        _ufoPopScore = pts;
        _ufoPopPos = new Vector2(_ufoX, 66f);
        _ufoPopTime = 1.2f;
        Explode(new Vector2(_ufoX, 76f), Red, 28f, 0.6f);
        _ufoLive = false;
        _ufoTimer = 18f + (float)_rng.NextDouble() * 12f;
    }

    void UpdateUfo(float dt)
    {
        if (_ufoLive)
        {
            _ufoX += _ufoDir * 120f * dt;
            if (_ufoX < -40f || _ufoX > FieldW + 40f)
            {
                _ufoLive = false;
                _ufoTimer = 18f + (float)_rng.NextDouble() * 12f;
            }
            return;
        }
        // keep the saucer away while the invaders are nearly done
        _ufoTimer -= dt;
        if (_ufoTimer <= 0 && _aliveCount > 8)
        {
            _ufoLive = true;
            _ufoDir = _rng.Next(2) == 0 ? 1 : -1;
            _ufoX = _ufoDir > 0 ? -40f : FieldW + 40f;
        }
    }

    // ===========================================================================================
    //  Drawing
    // ===========================================================================================
    float _scale;
    Vector2 _origin;

    Vector2 T(Vector2 p) => _origin + p * _scale;

    void Line(Vector2 a, Vector2 b, float w, Color c) => Gfx.Line(T(a), T(b), Math.Max(1f, w * _scale), c);

    void DrawSprite(Sprite s, Vector2 pos, float cell, Color color, float width = 2f)
    {
        foreach (var (a, b) in s.Edges)
            Line(pos + a * cell, pos + b * cell, width, color);
    }

    public void Draw()
    {
        int w = Gfx.Width, h = Gfx.Height;
        _scale = Math.Min((h - 20f) / FieldH, (w - 40f) / FieldW);
        _origin = new Vector2((w - FieldW * _scale) / 2f, (h - FieldH * _scale) / 2f);

        DrawBackdrop(w, h);
        DrawField();
        DrawHud(w, h);

        if (_state == State.Title)
            Panel(w, h, "VECTOR INVADERS", "press SPACE or ENTER to play",
                "LEFT / RIGHT (or A / D) move       SPACE fire",
                "shoot them all - the shields won't last forever");
        else if (_state == State.GameOver)
            Panel(w, h, "GAME OVER", _score >= _high && _score > 0 ? "NEW HIGH SCORE!" : $"score {_score:N0}",
                _stateTime > 1f ? "press SPACE or ENTER to play again" : "", "");
        else if (_state == State.WaveClear)
            Centered($"WAVE {_wave} CLEARED", (int)(h * 0.5f), 34, Amber);
    }

    void DrawField()
    {
        // top and bottom rules
        Line(new Vector2(10, 52), new Vector2(FieldW - 10, 52), 1.5f, Dim);
        Line(new Vector2(10, PlayerY + 36), new Vector2(FieldW - 10, PlayerY + 36), 2f, Green);

        // shields
        foreach (var sh in _shields)
        {
            sh.Edges ??= Outline((x, y) => sh.Cells[x, y], ShieldW, ShieldH);
            foreach (var (a, b) in sh.Edges)
                Line(sh.Pos + a * ShieldCell, sh.Pos + b * ShieldCell, 1.6f, Green);
        }

        // invaders
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_alive[r, c])
                    DrawSprite(InvaderSprites[RowType[r]][_frame], InvaderPos(r, c), Cell, RowColors[r]);

        // saucer
        if (_ufoLive)
            DrawSprite(UfoSprite, new Vector2(_ufoX - UfoSprite.W * Cell / 2f, 66f), Cell, Red);
        if (_ufoPopTime > 0)
            Gfx.Text($"{_ufoPopScore}", (int)T(_ufoPopPos).X - 16, (int)T(_ufoPopPos).Y, 18, Red);

        // cannon
        if (_state != State.Dying && _state != State.GameOver)
            DrawSprite(CannonSprite, new Vector2(_playerX - CannonSprite.W * Cell / 2f, PlayerY), Cell, Amber);

        // shot and bombs
        if (_bulletLive)
            Line(_bullet, _bullet + new Vector2(0, 12), 2.5f, Rock);
        foreach (var b in _bombs)
        {
            // a zig-zag bolt
            Vector2 p = b.Pos;
            float s = MathF.Sin(b.Wobble) * 3f;
            Line(p + new Vector2(s, 0), p + new Vector2(-s, 5), 2f, Red);
            Line(p + new Vector2(-s, 5), p + new Vector2(s, 10), 2f, Red);
        }

        // explosions
        foreach (var b in _bursts)
        {
            float t = b.Age / b.Life;
            float r1 = b.Size * (0.25f + 0.5f * t), r2 = b.Size * (0.5f + 0.7f * t);
            Color c = Gfx.Fade(b.Color, 1f - t);
            for (int i = 0; i < 10; i++)
            {
                float a = MathF.Tau * i / 10f + b.Size * 0.3f;
                var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                Line(b.Pos + d * r1, b.Pos + d * (i % 2 == 0 ? r2 : r2 * 0.7f), 2f, c);
            }
        }
    }

    void DrawBackdrop(int w, int h)
    {
        const float grid = 110f;
        float ox = _time * 6f % grid, oy = _time * 3f % grid;
        for (float x = -ox; x < w + grid; x += grid)
            for (float y = -oy; y < h + grid; y += grid)
            {
                Gfx.Line(new Vector2(x - 3, y), new Vector2(x + 3, y), 1f, Gfx.Fade(Dim, 0.45f));
                Gfx.Line(new Vector2(x, y - 3), new Vector2(x, y + 3), 1f, Gfx.Fade(Dim, 0.45f));
            }
    }

    void DrawHud(int w, int h)
    {
        int size = Math.Clamp((int)(h * 0.05f), 18, 40);
        int small = Math.Clamp((int)(h * 0.026f), 14, 20);

        float lx = Math.Max(10f, _origin.X - 250f);
        Gfx.Text("SCORE", (int)lx, 40, small, Dim);
        Gfx.Text($"{_score:N0}", (int)lx, 40 + small + 4, size, Green);
        Gfx.Text("HIGH", (int)lx, 40 + small + size + 28, small, Dim);
        Gfx.Text($"{_high:N0}", (int)lx, 40 + 2 * small + size + 32, size, Amber);

        float rx = _origin.X + FieldW * _scale + 20f;
        Gfx.Text("WAVE", (int)rx, 40, small, Dim);
        Gfx.Text($"{_wave}", (int)rx, 40 + small + 4, size, Green);
        Gfx.Text("LIVES", (int)rx, 40 + small + size + 28, small, Dim);
        for (int i = 0; i < Math.Max(0, _lives - (_state == State.Playing || _state == State.WaveClear ? 1 : 0)); i++)
        {
            // spare cannons; the one in play isn't counted
            var s = CannonSprite;
            float cell = 2f * _scale;
            Vector2 pos = new(rx + i * (s.W * cell + 8), 40 + 2 * small + size + 36);
            foreach (var (a, b) in s.Edges)
                Gfx.Line(pos + a * cell, pos + b * cell, 1.5f, Amber);
        }

        Gfx.Text("ESC  menu", 10, h - 28, 14, Dim);
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
}
