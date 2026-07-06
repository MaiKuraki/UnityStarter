namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// No-op diagnostics used as a safe default when a host does not inject a sink.
    /// All members are allocation-free and side-effect-free.
    /// </summary>
    public sealed class NullChoreographyDiagnostics : IChoreographyDiagnostics
    {
        public static readonly NullChoreographyDiagnostics Instance = new NullChoreographyDiagnostics();

        private NullChoreographyDiagnostics()
        {
        }

        public bool IsEnabled(ChoreographyLogLevel level) => false;

        public void Log(ChoreographyLogLevel level, string category, string message)
        {
        }
    }
}
