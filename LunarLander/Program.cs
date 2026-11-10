using LunarLander;
using WireGames;

ApplicationConfiguration.Initialize();
Application.Run(new GameHost("Lunar Lander", new LanderGame()));
