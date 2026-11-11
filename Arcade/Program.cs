using Arcade;
using WireGames;

ApplicationConfiguration.Initialize();

const string ArcadeTitle = "Wire Arcade";

GameHost host = null!;
MenuGame menu = null!;

var entries = new List<MenuEntry>
{
    new("RELATIVE ASTEROIDS",
        "Shoot the roving rocks. The ship stays put - the whole world turns around you.",
        MenuIcon.Asteroid,
        () => new RelativeAsteroids.Game()),
    new("LUNAR LANDER",
        "Undock from the mothership, land on a pad, then lift off and dock again.",
        MenuIcon.Lander,
        () => new LunarLander.LanderGame()),
    new("TANK ZONE",
        "First-person tank battle on a 3D wireframe plain. Hunt tanks, missiles and saucers - and watch the radar.",
        MenuIcon.Tank,
        () => new TankZone.TankGame()),
    new("TRENCH RUN",
        "A first-person assault on a battle station: dogfight in space, skim the surface, then fly the trench and hit the reactor port.",
        MenuIcon.Trench,
        () => new TrenchRun.TrenchGame()),
};

menu = new MenuGame(entries, entry =>
{
    host.Game = entry.Create();
    host.Text = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(entry.Title.ToLower());
});

host = new GameHost(ArcadeTitle, menu);

// Esc: leave a game and go back to the menu; Esc on the menu quits.
host.EscapePressed += () =>
{
    if (host.Game == menu)
    {
        host.Close();
    }
    else
    {
        host.Game = menu;
        host.Text = ArcadeTitle;
    }
};

Application.Run(host);
