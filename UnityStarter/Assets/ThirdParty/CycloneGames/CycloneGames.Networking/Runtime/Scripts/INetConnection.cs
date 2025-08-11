namespace CycloneGames.Networking
{
    public interface INetConnection
    {
        int ConnectionId { get; }
        string RemoteAddress { get; }
        bool IsConnected { get; }
        // True if the connection has passed authentication and is part of the game world.
        bool IsAuthenticated { get; }
    }
}