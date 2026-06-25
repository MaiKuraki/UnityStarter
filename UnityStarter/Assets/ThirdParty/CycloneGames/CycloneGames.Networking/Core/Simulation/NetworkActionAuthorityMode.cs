namespace CycloneGames.Networking.Simulation
{
    public enum NetworkActionAuthorityMode : byte
    {
        LocalOnly,
        ClientPredictedServerAuthoritative,
        ServerAuthoritative,
        Lockstep,
        Replay
    }
}
