using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using WireGames;

namespace Arcade;

enum MenuIcon { Asteroid, Lander }

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

        Centered("WIRE  ARCADE", 70, 64, Green);
        Gfx.Line(new Vector2(cx - 280, 150), new Vector2(cx + 280, 150), 2f, Dim);
        Centered("choose your game", 164, 20, Dim);

        float top = Math.Max(230, h / 2f - _entries.Count * 55);
        float left = cx - 340;
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            bool sel = i == _selected;
            float y = top + i * 120;
            Color main = sel ? Green : Dim;

            if (sel)
            {
                float pulse = 0.5f + 0.5f * MathF.Sin(_time * 6f);
                Color frame = Gfx.Fade(Green, 0.45f + 0.4f * pulse);
                Gfx.Rect(left - 20, y - 14, 680, 96, frame);
            }

            DrawIcon(e.Icon, new Vector2(left + 40, y + 34), sel ? Green : Dim, sel);
            Gfx.Text($"{i + 1}.  {e.Title}", (int)left + 100, (int)y, 34, main);
            Gfx.Text(e.Description, (int)left + 100, (int)y + 48, 17, sel ? Rock : Dim);
        }

        Centered("UP/DOWN select    ENTER start    ESC quit (ESC in a game returns here)", h - 40, 16, Dim);
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
