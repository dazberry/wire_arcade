# Wire Arcade

A small collection of wireframe arcade games written in C#, all sharing one idea:
**the ship never moves on screen.** It stays fixed in the centre of the window and the
whole world moves (and in Asteroids, rotates) around it.

| Game | What it is |
| --- | --- |
| **Relative Asteroids** | Classic Asteroids, but the ship is fixed in place, pointing up. Turning rotates the entire screen; thrusting makes the asteroids slide past. |
| **Lunar Lander** | Undock from a mothership, land on a pad, then lift off and dock again. The lander is fixed at the centre and the moon scrolls past underneath. |

Everything is drawn as simple lines with GDI+ (no GPU, OpenGL driver or third-party
packages required).

## Requirements

- Windows 10 or later (the games use WinForms / GDI+)
- [.NET 9 SDK](https://dotnet.microsoft.com/download)

## Build and run

```bash
dotnet build wire_arcade.sln
```

Start the menu, which lets you pick a game:

```bash
dotnet run -c Release --project Arcade
```

Or run a game directly:

```bash
dotnet run -c Release --project RelativeAsteroids
dotnet run -c Release --project LunarLander
```

In the menu use **Up/Down** and **Enter** (or the number keys) to choose a game.
**Esc** inside a game returns to the menu; **Esc** on the menu quits.

## Relative Asteroids

The world is a wrapping square. Everything is drawn relative to the ship and then rotated by
the inverse of its heading, so the ship stays put while the world turns around it.

| Key | Action |
| --- | --- |
| A / D or Left / Right | Turn (the world rotates) |
| W / Up | Thrust |
| S / Down | Reverse thrust |
| Space | Fire |
| P | Pause |
| Enter | Restart after game over |

Faint `+` markers are pinned to the world so you can see your own motion in empty space; the
compass at the top right shows where world "north" is. Large rocks split into medium ones, then
small ones.

## Lunar Lander

Rounds alternate between a **descent** and an **ascent**:

1. **Descent** - you start docked to the mothership. Steer the mothership left/right to line it
   up over a landing pad, press Enter to release the lander, then land on a pad. Narrower pads
   are worth more (x1, x2, x3, x5).
2. **Ascent** - take off from the pad and dock with the orbiting mothership above. A funnel of
   light fans out from its docking bay; match its speed and fly up the funnel into the bay.
   Docking refuels the lander and starts the next descent.

A landing needs both feet on a pad with a horizontal speed of at most 20 and a vertical speed of
at most 38. Docking needs you to be lined up with the bay, within 14 of the mothership's
horizontal speed, and rising slower than 32. Anything else is a crash.

| Key | Action |
| --- | --- |
| W / Up / Space | Main engine boost (only while held) |
| A / D or Left / Right | Side boosters (only while held) |
| Left / Right *while docked* | Slide the mothership |
| Enter / Space *while docked* | Release the lander |
| P | Pause |
| Enter | Continue after a landing, docking or crash |

The strip along the bottom of the screen is a radar showing the whole moon laid out flat: each
landing pad with its multiplier and distance, and the mothership (`MS`).

## Project layout

```
wire_arcade.sln
WireGames/          shared library: GDI+ drawing helpers, keyboard input, window + game loop
RelativeAsteroids/  the Asteroids game
LunarLander/        the Lunar Lander game
Arcade/             the start menu that launches either game (WireArcade.exe)
```

Each game implements `WireGames.IGame` (`Update(dt)` and `Draw()`). To add a game, create a
project that references `WireGames`, implement `IGame`, and add an entry to the list in
`Arcade/Program.cs`.

## Licence

[MIT](LICENSE)
