using System.Diagnostics;
using System.Windows.Forms;

namespace WireGames;

/// <summary>A game: advance the simulation, then draw it with <see cref="Gfx"/>.</summary>
public interface IGame
{
    void Update(float dt);
    void Draw();
}

/// <summary>Window + game loop shared by every wireframe game.</summary>
public sealed class GameHost : Form
{
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 8 };
    double _last;

    /// <summary>The game currently running. Can be swapped at any time (e.g. by a menu).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IGame Game { get; set; }

    /// <summary>
    /// Raised when Escape is pressed. If nothing is subscribed, Escape closes the window.
    /// </summary>
    public event Action? EscapePressed;

    public GameHost(string title, IGame game)
    {
        Game = game;
        Text = title;
        ClientSize = new System.Drawing.Size(1280, 720);
        MinimumSize = new System.Drawing.Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = System.Drawing.Color.Black;
        KeyPreview = true;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        DoubleBuffered = true;

        _last = _clock.Elapsed.TotalSeconds;
        _timer.Tick += (_, _) =>
        {
            double now = _clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(now - _last, 0.05);
            _last = now;
            Game.Update(dt);
            Input.EndFrame();
            Invalidate();
        };
        _timer.Start();

        Deactivate += (_, _) => Input.ReleaseAll(); // don't leave keys "stuck" on alt-tab
    }

    protected override bool IsInputKey(Keys keyData) => true; // let arrow keys through to KeyDown

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            if (EscapePressed is { } handler)
                handler();
            else
                Close();
            return;
        }
        Input.KeyDown(e.KeyCode);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Input.KeyUp(e.KeyCode);
        e.Handled = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        Gfx.Begin(e.Graphics, ClientSize.Width, ClientSize.Height);
        Game.Draw();
    }
}
