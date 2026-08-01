namespace CycloneGames.Logging
{
    /// <summary>
    /// Read-only operational surface for diagnostics and memory-governance integrations.
    /// Implementations expose observations without granting lifecycle or sink ownership.
    /// </summary>
    public interface ILogPipelineMonitor
    {
        bool IsFaulted { get; }

        LogPipelineStatistics GetStatistics();
    }
}
