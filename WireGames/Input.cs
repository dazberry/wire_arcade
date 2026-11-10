using System.Windows.Forms;

namespace WireGames;

/// <summary>Keyboard state fed by <see cref="GameHost"/>. <see cref="Pressed"/> is true for one frame after a key goes down.</summary>
public static class Input
{
    static readonly HashSet<Keys> Held = new();
    static readonly HashSet<Keys> Hit = new();

    public static void KeyDown(Keys k)
    {
        if (Held.Add(k))
            Hit.Add(k);
    }

    public static void KeyUp(Keys k) => Held.Remove(k);

    public static void ReleaseAll() => Held.Clear();

    public static bool Down(Keys k) => Held.Contains(k);

    public static bool Pressed(Keys k) => Hit.Contains(k);

    public static void EndFrame() => Hit.Clear();
}
