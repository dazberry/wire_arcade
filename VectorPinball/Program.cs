using VectorPinball;
using WireGames;

ApplicationConfiguration.Initialize();
Application.Run(new GameHost("Vector Pinball", new PinballGame()));
