namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// How a notification bus handles a subscriber that throws during
    /// <see cref="EventBus{T}.Publish"/>. This mirrors the re-entrancy policy: observable, deliberate,
    /// and never silently swallowed without a record.
    /// </summary>
    public enum PublishErrorPolicy
    {
        /// <summary>
        /// The first subscriber exception propagates and halts dispatch of the remaining handlers.
        /// Fail-loud and predictable; the default.
        /// </summary>
        Stop = 0,

        /// <summary>
        /// A subscriber exception is logged through the cold-path sink and dispatch continues to the
        /// remaining handlers. Use this for production resilience so one broken UI subscriber cannot
        /// take down the whole notification chain.
        /// </summary>
        Swallow = 1,
    }
}
