using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Signals that a mutation was rejected because the owning AbilitySystemComponent is iterating
    /// or already mutating active effects. The requested operation did not run and no state changed.
    /// </summary>
    public sealed class GASReentrancyRejectedException : InvalidOperationException
    {
        public GASReentrancyRejectedException(string message) : base(message)
        {
        }
    }
}
