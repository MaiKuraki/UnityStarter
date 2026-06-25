namespace CycloneGames.Networking.Simulation
{
    public enum NetworkActionResultCode : byte
    {
        Accepted,
        Corrected,
        Rejected,
        Duplicate,
        OutOfOrder,
        Expired,
        Unauthorized,
        InvalidPayload,
        SimulationMismatch,
        RateLimited,
        Conflict
    }
}
