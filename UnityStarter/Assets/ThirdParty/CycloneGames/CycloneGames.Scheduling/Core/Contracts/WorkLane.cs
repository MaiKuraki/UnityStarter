namespace CycloneGames.Scheduling.Core
{
    /// <summary>
    /// Admission priority; lower value runs first. A non-empty lane never waits longer than
    /// <see cref="WorkScheduler.StarvationLimitTicks"/> ticks for a step.
    /// </summary>
    public enum WorkLane : byte
    {
        /// <summary>Must make progress this frame. Bypasses lower lanes.</summary>
        Immediate = 0,

        High = 1,

        /// <summary>Default lane.</summary>
        Normal = 2,

        /// <summary>Idle-time maintenance.</summary>
        Background = 3,
    }
}
