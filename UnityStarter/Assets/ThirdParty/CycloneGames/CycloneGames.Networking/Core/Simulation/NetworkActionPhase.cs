namespace CycloneGames.Networking.Simulation
{
    public enum NetworkActionPhase : byte
    {
        None,
        Requested,
        Predicted,
        Confirmed,
        Rejected,
        Applied,
        Corrected,
        Cancelled
    }
}
