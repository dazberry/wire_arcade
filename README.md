# Wire Arcade

A small collection of wireframe arcade games written in C#, all sharing one idea:
**the ship never moves on screen.** It stays fixed in the centre of the window and the
whole world moves (and in Asteroids, rotates) around it.

| Game | What it is |
| --- | --- |
| **Relative Asteroids** | Classic Asteroids, but the ship is fixed in place, pointing up. Turning rotates the entire screen; thrusting makes the asteroids slide past. |
| **Lunar Lander** | Undock from a mothership, land on a pad, then lift off and dock again. The lander is fixed at the centre and the moon scrolls past underneath. |
| **Tank Zone** | A first-person 3D wireframe tank battle, inspired by the 1980 Atari arcade game *Battlezone*. Hunt tanks, missiles and flying saucers on a plain of pyramids and blocks. |
| **Trench Run** | A first-person space assault, inspired by Atari's 1983 vector arcade game *Star Wars*. Dogfight in space, skim the surface of a battle station, then fly the trench and put a torpedo into the reactor port. |

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
dotnet run -c Release --project TankZone
dotnet run -c Release --project TrenchRun
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

## Tank Zone

A first-person tank game with a small 3D wireframe engine (perspective projection with near-plane
clipping). You are in a tank on a flat plain dotted with pyramids and blocks that block movement
and shells. A mountain range, a crescent moon and an erupting volcano sit on the horizon. The
enemy closes in from all around, so keep an eye on the radar at the top.

| Key | Action |
| --- | --- |
| W / Up | Drive forward |
| S / Down | Reverse (slower) |
| A / D or Left / Right | Turn the tank |
| Space | Fire (one shell at a time) |
| G | Toggle a ground grid (handy for judging your own movement) |
| P | Pause |
| Enter | Restart after game over |

**Enemies and scoring**

| Enemy | Points | Notes |
| --- | --- | --- |
| Tank | 1,000 | Slow. Can't fire for 2 seconds after appearing. |
| Missile | 2,000 | Appears from 5,000 points. Fast and small; hops over obstacles; later ones swerve. |
| Super tank | 3,000 | Replaces ordinary tanks after the sixth missile. Faster and quicker to fire. |
| Flying saucer | 5,000 | From 2,000 points. Doesn't attack and isn't on the radar; shoot it down for a bonus. A flashing "FLYING SAUCER!" warns you it's about. |

- While the enemy is ahead on points, tanks appear in front of you and shoot poorly. Once you lead
  by 7,000 they appear from any direction and attack with full aggression. Every time you are
  destroyed the enemy's score goes up by 1,000.
- You start with 3 tanks and earn a bonus tank at 15,000 and again at 100,000 points.
- When you are hit the windshield cracks.
- The radar shows enemy tanks and missiles (not saucers or obstacles); the lines mark your 90 degree
  field of view. A status line reports `ENEMY IN RANGE`, `ENEMY TO LEFT/RIGHT/REAR` and
  `MOTION BLOCKED BY OBJECT`.

This is a from-scratch homage rather than a port, so details (obstacle layout, timings, exact AI) are
approximations.

## Trench Run

You pilot a fighter on rails; there is no ship to see, just a targeting cross-hair. The arrow keys
(or WASD) move the cross-hair, and in the surface and trench stages your ship follows it, so aiming
and steering are one control. Each wave has three stages:

1. **Dogfight** - enemy interceptors swoop in and spit spinning fireballs at you. Shoot the
   interceptors (1,000) or the fireballs (33) before they reach you. Shoot enough and the rest break off.
2. **Surface run** - skim the battle station's surface. Laser bunkers (200) fire at you. From wave 2
   towers stand in your way: hitting one costs a shield, shooting the top scores 200 for the first,
   400 for the second and so on, and clearing *every* tower top in the wave is worth 50,000.
3. **Trench run** - fly the trench past wall and floor turrets (100). From wave 2 catwalks span the
   trench: fly over or under them. Interceptors come down the trench, and partway along a dark
   fighter appears. It can't be destroyed, but 2,000 points a hit drives it off. Near the end the guns
   switch to a single proton torpedo: put it into the reactor port (25,000) to destroy the station. A
   miss costs a shield and sends you round again. Fire **nothing** else in the trench and you earn the
   "Use the Force" bonus (5,000 in wave 1, rising to 100,000 by wave 5).

You have six deflector shields. Fireballs, towers and catwalks each cost one, and the game ends when
the last is gone. Destroying a station restores a shield and pays 5,000 per shield remaining. Every
wave the enemy gets faster and fires more often, the stages get longer and the dark fighter takes
more hits to drive off.

| Key | Action |
| --- | --- |
| Arrows / WASD | Move the cross-hair (and steer in the surface and trench stages) |
| Space | Fire lasers (hold for rapid fire); fire the torpedo near the reactor |
| P | Pause |
| Enter | Restart after game over |

Like Tank Zone this is a from-scratch homage rather than a port, so layouts, timings and point values
are approximations of the arcade game.

## Project layout

```
wire_arcade.sln
WireGames/          shared library: GDI+ drawing helpers, wireframe 3D models, keyboard input, window + game loop
RelativeAsteroids/  the Asteroids game
LunarLander/        the Lunar Lander game
TankZone/           the first-person 3D tank game
TrenchRun/          the first-person 3D space assault game
Arcade/             the start menu that launches either game (WireArcade.exe)
```

Each game implements `WireGames.IGame` (`Update(dt)` and `Draw()`). To add a game, create a
project that references `WireGames`, implement `IGame`, and add an entry to the list in
`Arcade/Program.cs`.

## Licence

[MIT](LICENSE)
