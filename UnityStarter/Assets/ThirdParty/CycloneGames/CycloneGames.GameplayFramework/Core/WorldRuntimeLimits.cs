using System;

namespace CycloneGames.GameplayFramework.Core
{
    /// <summary>
    /// Immutable construction-time memory and admission limits for one gameplay World.
    /// </summary>
    public sealed class WorldRuntimeLimits
    {
        public const int MaximumSupportedActorCount = 65_536;

        public static WorldRuntimeLimits Default { get; } = new WorldRuntimeLimits();

        public WorldRuntimeLimits(
            int maximumActorCount = MaximumSupportedActorCount,
            int initialActorCapacity = 128,
            int initialUpdateTickCapacity = 128,
            int initialFixedUpdateTickCapacity = 32,
            int initialLateUpdateTickCapacity = 32)
        {
            if (maximumActorCount <= 0 || maximumActorCount > MaximumSupportedActorCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumActorCount),
                    maximumActorCount,
                    $"World actor capacity must be between 1 and {MaximumSupportedActorCount}.");
            }

            MaximumActorCount = maximumActorCount;
            InitialActorCapacity = NormalizeInitialCapacity(
                initialActorCapacity,
                maximumActorCount,
                nameof(initialActorCapacity));
            InitialUpdateTickCapacity = NormalizeInitialCapacity(
                initialUpdateTickCapacity,
                maximumActorCount,
                nameof(initialUpdateTickCapacity));
            InitialFixedUpdateTickCapacity = NormalizeInitialCapacity(
                initialFixedUpdateTickCapacity,
                maximumActorCount,
                nameof(initialFixedUpdateTickCapacity));
            InitialLateUpdateTickCapacity = NormalizeInitialCapacity(
                initialLateUpdateTickCapacity,
                maximumActorCount,
                nameof(initialLateUpdateTickCapacity));
        }

        public int MaximumActorCount { get; }
        public int InitialActorCapacity { get; }
        public int InitialUpdateTickCapacity { get; }
        public int InitialFixedUpdateTickCapacity { get; }
        public int InitialLateUpdateTickCapacity { get; }

        private static int NormalizeInitialCapacity(int value, int maximum, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Initial capacity cannot be negative.");
            }

            return Math.Min(value, maximum);
        }
    }
}
