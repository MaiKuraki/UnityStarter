namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Severity levels for choreography diagnostics. Kept minimal and engine-independent.
    /// </summary>
    public enum ChoreographyLogLevel : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>
    /// Lightweight diagnostic sink for the Core layer. The Core must never depend on a Unity or third-party
    /// logger directly; hosts inject an implementation (e.g. a CycloneGames.Logger bridge in the Unity layer).
    /// Implementations should be cheap and must tolerate being called from playback hot paths.
    /// </summary>
    public interface IChoreographyDiagnostics
    {
        bool IsEnabled(ChoreographyLogLevel level);

        void Log(ChoreographyLogLevel level, string category, string message);
    }
}
