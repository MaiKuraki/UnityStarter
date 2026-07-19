namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>Authoritative lifecycle state for a managed window instance.</summary>
    public enum UIWindowState
    {
        Created = 0,
        Opening = 1,
        Open = 2,
        Closing = 3,
        Closed = 4,
    }
}
