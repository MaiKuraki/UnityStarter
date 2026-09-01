namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// How a notification bus treats a subscriber that throws during
    /// <see cref="EventBus{T}.Publish"/>. The choice is deliberate and observable: no policy ever
    /// discards a fault without leaving a counter or an exception behind.
    /// </summary>
    public enum PublishErrorPolicy
    {
        /// <summary>
        /// The first subscriber exception propagates immediately and the remaining handlers are
        /// skipped. The stack trace of the original throw site is preserved.
        ///
        /// Predictable and fail-loud, but in a large project one broken subscriber silently costs
        /// every later subscriber its delivery. Prefer <see cref="ContinueOnError"/> once a bus has
        /// more than a handful of subscribers.
        /// </summary>
        Stop = 0,

        /// <summary>
        /// A subscriber exception is logged through the cold-path sink and counted in
        /// <see cref="EventBus{T}.SubscriberErrorCount"/>; dispatch continues to every remaining
        /// handler and the publish returns normally.
        ///
        /// Correct for best-effort presentation listeners (damage numbers, audio, VFX) where a fault
        /// must never affect gameplay. Do not use it for logic that must not silently fail.
        /// </summary>
        Swallow = 1,

        /// <summary>
        /// Every subscriber runs, then the first exception is rethrown with its original stack.
        /// The recommended default for large projects: no subscriber is skipped and no fault is lost.
        ///
        /// Later exceptions in the same round are logged and counted but not rethrown, because the
        /// first fault is the root cause and the rest are usually fallout.
        /// </summary>
        ContinueOnError = 2,
    }
}
