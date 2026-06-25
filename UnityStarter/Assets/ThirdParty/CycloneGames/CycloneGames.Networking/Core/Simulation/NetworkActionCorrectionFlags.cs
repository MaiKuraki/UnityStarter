using System;

namespace CycloneGames.Networking.Simulation
{
    [Flags]
    public enum NetworkActionCorrectionFlags : ushort
    {
        None = 0,
        State = 1 << 0,
        Transform = 1 << 1,
        Velocity = 1 << 2,
        Timeline = 1 << 3,
        Ownership = 1 << 4,
        FullReset = 1 << 15
    }
}
