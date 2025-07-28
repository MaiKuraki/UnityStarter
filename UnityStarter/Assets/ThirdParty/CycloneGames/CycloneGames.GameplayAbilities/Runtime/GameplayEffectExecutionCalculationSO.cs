using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A ScriptableObject that acts as a factory for a runtime GameplayEffectExecutionCalculation instance.
    /// This allows for data-driven configuration of execution logic within the Unity Editor.
    /// </summary>
    public abstract class GameplayEffectExecutionCalculationSO : ScriptableObject
    {
        /// <summary>
        /// Factory method to create a new runtime instance of the execution logic.
        /// </summary>
        /// <returns>A new instance of a GameplayEffectExecutionCalculation subclass.</returns>
        public abstract GameplayEffectExecutionCalculation CreateExecution();
    }
}