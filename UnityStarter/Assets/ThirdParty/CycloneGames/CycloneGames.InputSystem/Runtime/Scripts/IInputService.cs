using R3;
using System;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Defines the public contract for a single player's input service.
    /// It provides reactive streams for actions and methods to manage input contexts.
    /// </summary>
    public interface IInputService
    {
        /// <summary>
        /// A read-only reactive property holding the name of the currently active context.
        /// </summary>
        ReadOnlyReactiveProperty<string> ActiveContextName { get; }

        /// <summary>
        /// An event that fires when the active context changes.
        /// </summary>
        event Action<string> OnContextChanged;

        /// <summary>
        /// Gets a reactive stream for a Vector2-based action (e.g., movement, aiming).
        /// </summary>
        /// <param name="actionName">The name of the action defined in the configuration.</param>
        /// <returns>An Observable stream of Vector2 values.</returns>
        Observable<Vector2> GetVector2Observable(string actionName);

        /// <summary>
        /// Gets a reactive stream for a button-based action (e.g., jump, shoot).
        /// </summary>
        /// <param name="actionName">The name of the action defined in the configuration.</param>
        /// <returns>An Observable stream of Unit values, signaling an activation.</returns>
        Observable<Unit> GetButtonObservable(string actionName);

        /// <summary>
        /// Registers a pre-configured InputContext, making it available for activation.
        /// </summary>
        /// <param name="context">The context object to register.</param>
        void RegisterContext(InputContext context);

        /// <summary>
        /// Pushes a context onto the top of the stack, making it the active context.
        /// </summary>
        /// <param name="contextName">The name of the context to activate.</param>
        void PushContext(string contextName);

        /// <summary>
        /// Pops the current context from the top of the stack, activating the one below it.
        /// </summary>
        void PopContext();

        /// <summary>
        /// Disables all input processing for this service instance.
        /// </summary>
        void BlockInput();

        /// <summary>
        /// Resumes input processing for this service instance.
        /// </summary>
        void UnblockInput();
    }
}