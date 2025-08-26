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
        /// The last active input device kind for this player (reactive).
        /// Updates whenever any bound action is performed.
        /// </summary>
        ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind { get; }

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
        /// Gets a reactive stream for a Vector2-based action within a specific action map.
        /// </summary>
        /// <param name="actionMapName">The action map that owns the action.</param>
        /// <param name="actionName">The action name.</param>
        /// <returns>An Observable stream of Vector2 values.</returns>
        Observable<Vector2> GetVector2Observable(string actionMapName, string actionName);

        /// <summary>
        /// Gets a reactive stream for a button-based action (e.g., jump, shoot).
        /// </summary>
        /// <param name="actionName">The name of the action defined in the configuration.</param>
        /// <returns>An Observable stream of Unit values, signaling an activation.</returns>
        Observable<Unit> GetButtonObservable(string actionName);

        /// <summary>
        /// Gets a reactive stream that fires when the button is held for at least the configured long-press duration.
        /// If the action has no long-press configured, returns an empty stream.
        /// </summary>
        /// <param name="actionName">The name of the action defined in the configuration.</param>
        /// <returns>An Observable stream of Unit values signaling a long-press.</returns>
        Observable<Unit> GetLongPressObservable(string actionName);

        /// <summary>
        /// Gets a reactive stream for the pressed state of a button action.
        /// Emits true on press start, false on release.
        /// </summary>
        /// <param name="actionName">The name of the action defined in the configuration.</param>
        /// <returns>An Observable stream of bool values indicating pressed state.</returns>
        Observable<bool> GetPressStateObservable(string actionName);

        /// <summary>
        /// Gets a reactive stream for a button-based action within a specific action map.
        /// </summary>
        /// <param name="actionMapName">The action map that owns the action.</param>
        /// <param name="actionName">The action name.</param>
        /// <returns>An Observable stream of Unit values.</returns>
        Observable<Unit> GetButtonObservable(string actionMapName, string actionName);

        /// <summary>
        /// Gets a reactive stream for a long-press event within a specific action map.
        /// Returns empty if the action has no long-press configured.
        /// </summary>
        /// <param name="actionMapName">The action map that owns the action.</param>
        /// <param name="actionName">The action name.</param>
        /// <returns>An Observable stream of Unit values signaling a long-press.</returns>
        Observable<Unit> GetLongPressObservable(string actionMapName, string actionName);

        /// <summary>
        /// Gets a reactive stream for the pressed state within a specific action map.
        /// Emits true on press start, false on release.
        /// </summary>
        /// <param name="actionMapName">The action map that owns the action.</param>
        /// <param name="actionName">The action name.</param>
        /// <returns>An Observable stream of bool values indicating pressed state.</returns>
        Observable<bool> GetPressStateObservable(string actionMapName, string actionName);

        /// <summary>
        /// Gets a reactive stream for a scalar-based action (e.g., zoom, sensitivity).
        /// </summary>
        /// <param name="actionName"></param>
        /// <returns></returns>
        Observable<float> GetScalarObservable(string actionName);

        /// <summary>
        /// Gets a reactive stream for a scalar-based action within a specific action map.
        /// </summary>
        /// <param name="actionMapName">The action map that owns the action.</param>
        /// <param name="actionName">The action name.</param>
        /// <returns>An Observable stream of float values.</returns>
        Observable<float> GetScalarObservable(string actionMapName, string actionName);

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