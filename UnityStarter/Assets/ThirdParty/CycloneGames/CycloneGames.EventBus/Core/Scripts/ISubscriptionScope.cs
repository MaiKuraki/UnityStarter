using System;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Aggregates several subscriptions so a game mode, UI window, or long task can release them all
    /// at once. This replaces string-named channels for lifecycle: scope disposal is structural, not
    /// string-routed, so it cannot leave half-torn-down state or ghost deliveries.
    /// </summary>
    public interface ISubscriptionScope : IDisposable
    {
        int Count { get; }

        void Add(IEventSubscription subscription);
    }
}
