namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Abstraction layer for animation control, supporting both Unity Animator and Animancer.
    /// </summary>
    public interface IAnimationController
    {
        /// <summary>
        /// Sets a float parameter value. Thread-safe if implementation supports it.
        /// </summary>
        void SetFloat(int parameterHash, float value);

        /// <summary>
        /// Sets a bool parameter value. Thread-safe if implementation supports it.
        /// </summary>
        void SetBool(int parameterHash, bool value);

        /// <summary>
        /// Triggers a trigger parameter. Thread-safe if implementation supports it.
        /// </summary>
        void SetTrigger(int parameterHash);

        /// <summary>
        /// Checks if the controller is valid and ready to use.
        /// </summary>
        bool IsValid { get; }
    }
}