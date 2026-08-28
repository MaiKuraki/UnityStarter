namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Which command backend the composition root selects. The enum itself is Core-owned; only the
    /// VitalRouter integration assembly knows how to build the VitalRouter backend.
    /// </summary>
    public enum CommandBackend
    {
        InProcess = 0,

        /// <summary>
        /// Reserved. The VitalRouter adapter cannot satisfy the struct-only
        /// <see cref="ICommandPublisher"/> port (it requires <c>VitalRouter.ICommand</c>), so this
        /// value is not routable through <see cref="EventBusContext.Commands"/>. Use the
        /// VitalRouter integration's <c>VitalRouterCommandPublisher</c> directly instead.
        /// </summary>
        VitalRouter = 1,
    }

    /// <summary>
    /// Overflow behavior for a bounded command queue.
    /// </summary>
    public enum CommandOverflowPolicy
    {
        Drop = 0,
        FailFast = 1,
    }
}
