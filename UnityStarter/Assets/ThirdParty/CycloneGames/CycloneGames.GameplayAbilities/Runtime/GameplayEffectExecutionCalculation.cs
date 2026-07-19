using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Stack-only, bounded output sink for one gameplay-effect execution.
    /// </summary>
    public readonly ref struct GameplayEffectExecutionOutput
    {
        private readonly List<ModifierInfo> modifiers;
        private readonly int capacity;

        internal GameplayEffectExecutionOutput(List<ModifierInfo> modifiers, int capacity)
        {
            this.modifiers = modifiers ?? throw new System.ArgumentNullException(nameof(modifiers));
            if (capacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(capacity), capacity, "Execution output capacity must be positive.");
            }

            this.capacity = capacity;
        }

        public int Count => modifiers.Count;
        public int Capacity => capacity;

        public void Add(ModifierInfo modifier)
        {
            if (modifier == null)
            {
                throw new System.ArgumentNullException(nameof(modifier));
            }
            if (modifiers.Count >= capacity)
            {
                throw new System.InvalidOperationException(
                    $"GameplayEffect execution output cannot exceed {capacity} modifiers.");
            }

            modifiers.Add(modifier);
        }
    }

    /// <summary>
    /// Base class for calculations that are too complex for a simple modifier.
    /// ExecutionCalculations can read any number of attributes from the source and target,
    /// perform complex logic, and then output modifications to any number of attributes.
    /// These are typically NOT predicted and run on the server.
    /// </summary>
    public abstract class GameplayEffectExecutionCalculation
    {
        /// <summary>
        /// Performs the execution logic.
        /// </summary>
        /// <param name="spec">The spec of the gameplay effect being executed.</param>
        /// <param name="executionOutput">A stack-only bounded sink for outgoing modifier results.</param>
        public abstract void Execute(GameplayEffectSpec spec, GameplayEffectExecutionOutput executionOutput);
    }
}
