namespace CycloneGames.Networking.Simulation
{
    public enum NetworkActionResultCode : byte
    {
        Invalid = 0,
        Accepted = 1,
        Corrected = 2,
        Rejected = 3,
        Duplicate = 4,
        OutOfOrder = 5,
        Expired = 6,
        Unauthorized = 7,
        InvalidPayload = 8,
        SimulationMismatch = 9,
        RateLimited = 10,
        Conflict = 11
    }
}
