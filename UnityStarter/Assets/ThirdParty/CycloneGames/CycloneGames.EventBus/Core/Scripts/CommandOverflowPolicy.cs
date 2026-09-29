namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Overflow behavior for a bounded command queue.
    /// </summary>
    public enum CommandOverflowPolicy
    {
        Drop = 0,
        FailFast = 1,
    }
}
