using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
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
        /// <param name="executionOutput">A collection to add outgoing modifier results to.</param>
        public abstract void Execute(GameplayEffectSpec spec, ref List<ModifierInfo> executionOutput);
    }
}