using VectorInvaders;
using WireGames;

ApplicationConfiguration.Initialize();
Application.Run(new GameHost("Vector Invaders", new InvadersGame()));
