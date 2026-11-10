using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace WireGames;

/// <summary>Thin GDI+ drawing helpers (no GPU / OpenGL driver required).</summary>
public static class Gfx
{
    static Graphics _g = null!;
    static readonly Dictionary<(int argb, float w), Pen> Pens = new();
    static readonly Dictionary<int, SolidBrush> Brushes = new();
    static readonly Dictionary<int, Font> Fonts = new();

    public static int Width { get; private set; }
    public static int Height { get; private set; }

    public static void Begin(Graphics g, int width, int height)
    {
        _g = g;
        Width = width;
        Height = height;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Black);
    }

    /// <summary>Quantises alpha so we only ever create a handful of distinct pens.</summary>
    public static Color Fade(Color c, float alpha)
    {
        int a = (int)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 16f) * 255 / 16;
        return Color.FromArgb(a, c);
    }

    static Pen PenFor(Color c, float width)
    {
        var key = (c.ToArgb(), width);
        if (!Pens.TryGetValue(key, out var pen))
        {
            pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            Pens[key] = pen;
        }
        return pen;
    }

    static Brush BrushFor(Color c)
    {
        int key = c.ToArgb();
        if (!Brushes.TryGetValue(key, out var b))
            Brushes[key] = b = new SolidBrush(c);
        return b;
    }

    static Font FontFor(int size)
    {
        if (!Fonts.TryGetValue(size, out var f))
            Fonts[size] = f = new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Pixel);
        return f;
    }

    public static void Line(Vector2 a, Vector2 b, float thickness, Color c) =>
        _g.DrawLine(PenFor(c, thickness), a.X, a.Y, b.X, b.Y);

    public static void Circle(Vector2 p, float r, Color c) =>
        _g.FillEllipse(BrushFor(c), p.X - r, p.Y - r, r * 2, r * 2);

    public static void CircleOutline(Vector2 p, float r, Color c) =>
        _g.DrawEllipse(PenFor(c, 1f), p.X - r, p.Y - r, r * 2, r * 2);

    public static void Rect(float x, float y, float w, float h, Color c) =>
        _g.DrawRectangle(PenFor(c, 1f), x, y, w, h);

    public static void FillRect(float x, float y, float w, float h, Color c) =>
        _g.FillRectangle(BrushFor(c), x, y, w, h);

    public static void Text(string text, int x, int y, int size, Color c) =>
        _g.DrawString(text, FontFor(size), BrushFor(c), x, y, StringFormat.GenericTypographic);

    public static int Measure(string text, int size) =>
        (int)_g.MeasureString(text, FontFor(size), int.MaxValue, StringFormat.GenericTypographic).Width;
}
