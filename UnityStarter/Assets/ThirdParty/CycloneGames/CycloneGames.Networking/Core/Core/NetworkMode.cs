namespace CycloneGames.Networking
{
    public enum NetworkMode : byte
    {
        Offline,
        Client,
        Server,
        Host,           // Server + local Client
        ListenServer,   // Player-hosted server (Monster Hunter, Pal World)
        DedicatedServer,
        Relay           // P2P via relay (Steam, Epic relay)
    }
}
