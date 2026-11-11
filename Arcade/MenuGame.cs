using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using WireGames;

namespace Arcade;

enum MenuIcon { Asteroid, Lander, Tank, Trench }

sealed record MenuEntry(string Title, string Description, MenuIcon Icon, Func<IGame> Create);

/// <summary>The start screen: pick a game with Up/Down (or 1-9) and press Enter.</summary>
sealed class MenuGame : IGame
{
    static readonly Color Green = Color.FromArgb(255, 120, 255, 150);
    static readonly Color Amber = Color.FromArgb(255, 255, 190, 80);
    static readonly Color Rock = Color.FromArgb(255, 225, 235, 255);
    static readonly Color Dim = Color.FromArgb(255, 70, 90, 80);

    readonly IReadOnlyList<MenuEntry> _entries;
    readonly Action<MenuEntry> _launch;
    int _selected;
    float _time;

    public MenuGame(IReadOnlyList<MenuEntry> entries, Action<MenuEntry> launch)
    {
        _entries = entries;
        _launch = launch;
    }

    public void Update(float dt)
    {
        _time += dt;

        if (Input.Pressed(Keys.Up) || Input.Pressed(Keys.W))
            _selected = (_selected + _entries.Count - 1) % _entries.Count;
        if (Input.Pressed(Keys.Down) || Input.Pressed(Keys.S))
            _selected = (_selected + 1) % _entries.Count;

        // Number keys jump straight to a game
        for (int i = 0; i < _entries.Count && i < 9; i++)
        {
            if (Input.Pressed(Keys.D1 + i) || Input.Pressed(Keys.NumPad1 + i))
            {
                _selected = i;
                _launch(_entries[i]);
                return;
            }
        }

        if (Input.Pressed(Keys.Enter) || Input.Pressed(Keys.Space))
            _launch(_entries[_selected]);
    }

    public void Draw()
    {
        int w = Gfx.Width, h = Gfx.Height;
        float cx = w / 2f;

        DrawBackdrop(w, h);

        // Each entry sits in a frame; descriptions wrap inside it so they never spill past the edge.
        // If the roomy layout doesn't fit the window, fall back to a compact one so every game stays on screen.
        const int textIndent = 100;   // room for the icon, from the frame's left edge
        const int textPadRight = 24;
        float frameW = Math.Min(760f, w - 60f);
        float frameLeft = cx - frameW / 2f;
        int textWidth = (int)frameW - textIndent - textPadRight;
        float bottomLimit = h - 55f;

        Layout lay = Layout.Roomy;
        if (!Fits(lay, textWidth, bottomLimit))
            lay = Layout.Compact;

        Centered("WIRE  ARCADE", lay.TitleY, lay.TitleSize, Green);
        Gfx.Line(new Vector2(cx - 280, lay.RuleY), new Vector2(cx + 280, lay.RuleY), 2f, Dim);
        Centered("choose your game", lay.SubY, 20, Dim);

        float y = lay.StartY;
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            bool sel = i == _selected;
            Color main = sel ? Green : Dim;
            List<string> lines = Wrap(e.Description, lay.DescSize, textWidth);
            float frameH = lay.FrameHeight(lines.Count);

            if (sel)
            {
                float pulse = 0.5f + 0.5f * MathF.Sin(_time * 6f);
                Color frame = Gfx.Fade(Green, 0.45f + 0.4f * pulse);
                Gfx.Rect(frameLeft, y, frameW, frameH, frame);
            }

            DrawIcon(e.Icon, new Vector2(frameLeft + 50, y + frameH / 2f), sel ? Green : Dim, sel);
            Gfx.Text($"{i + 1}.  {e.Title}", (int)frameLeft + textIndent, (int)y + lay.PadTop, lay.EntryTitleSize, main);
            for (int l = 0; l < lines.Count; l++)
                Gfx.Text(lines[l], (int)frameLeft + textIndent, (int)y + lay.PadTop + lay.TitleToDesc + l * lay.DescLine, lay.DescSize, sel ? Rock : Dim);

            y += frameH + lay.Gap;
        }

        Centered("UP/DOWN select    ENTER start    ESC quit (ESC in a game returns here)", h - 40, 16, Dim);
    }

    /// <summary>Sizes and spacing for the entry list.</summary>
    sealed record Layout(
        int TitleY, int TitleSize, int RuleY, int SubY, float StartY,
        int EntryTitleSize, int DescSize, int DescLine, int PadTop, int TitleToDesc, int PadBottom, int Gap)
    {
        public static readonly Layout Roomy = new(70, 64, 150, 164, 230f, 34, 17, 22, 14, 48, 10, 26);
        public static readonly Layout Compact = new(30, 48, 100, 108, 150f, 28, 16, 20, 10, 40, 8, 16);

        public float FrameHeight(int descLines) => PadTop + TitleToDesc + descLines * DescLine + PadBottom;
    }

    bool Fits(Layout lay, int textWidth, float bottomLimit)
    {
        float y = lay.StartY;
        foreach (var e in _entries)
            y += lay.FrameHeight(Wrap(e.Description, lay.DescSize, textWidth).Count) + lay.Gap;
        return y - lay.Gap <= bottomLimit;
    }

    /// <summary>Greedy word-wrap so that no line is wider than <paramref name="maxWidth"/> pixels.</summary>
    static List<string> Wrap(string text, int size, int maxWidth)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && Gfx.Measure(candidate, size) > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }

    /// <summary>Slowly drifting "+" markers, like the ones in the games.</summary>
    void DrawBackdrop(int w, int h)
    {
        const float grid = 110f;
        float ox = _time * 14f % grid;
        float oy = _time * 6f % grid;
        for (float x = -ox; x < w + grid; x += grid)
        {
            for (float y = -oy; y < h + grid; y += grid)
            {
                Gfx.Line(new Vector2(x - 4, y), new Vector2(x + 4, y), 1f, Gfx.Fade(Dim, 0.6f));
                Gfx.Line(new Vector2(x, y - 4), new Vector2(x, y + 4), 1f, Gfx.Fade(Dim, 0.6f));
            }
        }
    }

    void DrawIcon(MenuIcon icon, Vector2 c, Color color, bool spin)
    {
        switch (icon)
        {
            case MenuIcon.Asteroid:
            {
                const int n = 11;
                float rot = spin ? _time * 0.9f : 0.4f;
                Vector2 prev = default;
                Vector2 first = default;
                for (int i = 0; i < n; i++)
                {
                    float a = rot + i * MathF.Tau / n;
                    float r = 26f * (0.78f + 0.22f * MathF.Sin(i * 2.3f + 1f));
                    Vector2 p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                    if (i == 0) first = p; else Gfx.Line(prev, p, 1.6f, color);
                    prev = p;
                }
                Gfx.Line(prev, first, 1.6f, color);
                break;
            }

            case MenuIcon.Trench:
            {
                // looking down a trench: two walls converging on a vanishing point, with cross-bars
                Vector2 P(float x, float y) => c + new Vector2(x, y) * 1.4f;
                Gfx.Line(P(-20, 16), P(-3, -3), 1.6f, color);
                Gfx.Line(P(20, 16), P(3, -3), 1.6f, color);
                Gfx.Line(P(-20, -16), P(-3, -5), 1.6f, color);
                Gfx.Line(P(20, -16), P(3, -5), 1.6f, color);
                Gfx.Line(P(-3, -3), P(3, -3), 1.6f, color);
                Gfx.Line(P(-3, -5), P(3, -5), 1.6f, color);
                Gfx.Line(P(-11, 8), P(11, 8), 1.2f, color);
                Gfx.Line(P(-7, 2), P(7, 2), 1.2f, color);
                // a gun sight, pulsing when selected
                float s = spin ? 5f + 2f * MathF.Sin(_time * 8f) : 5f;
                Gfx.Line(P(-s - 3, -4), P(-3, -4), 1.4f, Amber);
                Gfx.Line(P(3, -4), P(s + 3, -4), 1.4f, Amber);
                break;
            }

            case MenuIcon.Tank:
            {
                Vector2 P(float x, float y) => c + new Vector2(x, y) * 1.5f;
                Vector2[] hull = { P(-17, 8), P(17, 8), P(12, -2), P(-12, -2) };
                for (int i = 0; i < hull.Length; i++)
                    Gfx.Line(hull[i], hull[(i + 1) % hull.Length], 1.6f, color);
                Gfx.Line(P(-6, -2), P(-6, -9), 1.6f, color);
                Gfx.Line(P(-6, -9), P(6, -9), 1.6f, color);
                Gfx.Line(P(6, -9), P(7, -2), 1.6f, color);
                Gfx.Line(P(6, -6), P(22, -6), 1.6f, color);
                if (spin)
                {
                    // a muzzle flash when selected
                    float f = 4f + 4f * MathF.Abs(MathF.Sin(_time * 8f));
                    Gfx.Line(P(23, -6), P(23 + f, -6), 1.6f, Amber);
                }
                break;
            }

            case MenuIcon.Lander:
            {
                Vector2 P(float x, float y) => c + new Vector2(x, y) * 1.3f;
                Vector2[] cabin =
                {
                    P(-6, -16), P(6, -16), P(11, -9), P(11, -1), P(6, 4), P(-6, 4), P(-11, -1), P(-11, -9),
                };
                for (int i = 0; i < cabin.Length; i++)
                    Gfx.Line(cabin[i], cabin[(i + 1) % cabin.Length], 1.6f, color);
                Gfx.Line(P(-9, 2), P(-14, 15), 1.6f, color);
                Gfx.Line(P(9, 2), P(14, 15), 1.6f, color);
                Gfx.Line(P(-18, 15), P(-10, 15), 1.6f, color);
                Gfx.Line(P(10, 15), P(18, 15), 1.6f, color);
                if (spin)
                {
                    float f = 8f + 6f * MathF.Abs(MathF.Sin(_time * 14f));
                    Gfx.Line(P(-5, 6), P(0, 6 + f), 1.6f, Amber);
                    Gfx.Line(P(5, 6), P(0, 6 + f), 1.6f, Amber);
                }
                break;
            }
        }
    }

    static void Centered(string text, int y, int size, Color color) =>
        Gfx.Text(text, (Gfx.Width - Gfx.Measure(text, size)) / 2, y, size, color);
}
