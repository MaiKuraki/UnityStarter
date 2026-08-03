namespace CycloneGames.Logging
{
    /// <summary>
    /// Ordered logging severity shared by every CycloneGames package.
    /// </summary>
    public enum LogSeverity : byte
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5,
        None = 6
    }
}
