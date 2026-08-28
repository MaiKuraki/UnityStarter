namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Minimal severity levels for EventBus diagnostics. Kept Core-owned so the Core layer never
    /// depends on a concrete logging package; an integration adapter maps these to a real backend.
    /// </summary>
    public enum EventBusLogSeverity
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }
}
