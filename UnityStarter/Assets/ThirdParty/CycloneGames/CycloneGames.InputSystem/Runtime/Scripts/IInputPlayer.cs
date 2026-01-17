using R3;
using System;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Public contract for a single player's input. Provides reactive streams and context management.
    /// </summary>
    public interface IInputPlayer
    {
        ReadOnlyReactiveProperty<string> ActiveContextName { get; }
        ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind { get; }
        event Action<string> OnContextChanged;

        /// <summary>
        /// Returns an observable that only emits ActiveDeviceKind changes when the specified context is active (at the top of the stack).
        /// This makes device kind subscriptions behave like context bindings - only the top context responds.
        /// </summary>
        Observable<InputDeviceKind> GetActiveDeviceKindObservableForContext(InputContext context);

        Observable<Vector2> GetVector2Observable(string actionName);
        Observable<Vector2> GetVector2Observable(string actionMapName, string actionName);
        Observable<Vector2> GetVector2Observable(int actionId);

        Observable<Unit> GetButtonObservable(string actionName);
        Observable<Unit> GetButtonObservable(string actionMapName, string actionName);
        Observable<Unit> GetButtonObservable(int actionId);

        /// <summary>
        /// Fires when button is held for configured long-press duration. Returns empty if not configured.
        /// </summary>
        Observable<Unit> GetLongPressObservable(string actionName);
        Observable<Unit> GetLongPressObservable(string actionMapName, string actionName);
        Observable<Unit> GetLongPressObservable(int actionId);

        /// <summary>
        /// Emits true on press start, false on release.
        /// </summary>
        Observable<bool> GetPressStateObservable(string actionName);
        Observable<bool> GetPressStateObservable(string actionMapName, string actionName);
        Observable<bool> GetPressStateObservable(int actionId);

        Observable<float> GetScalarObservable(string actionName);
        Observable<float> GetScalarObservable(string actionMapName, string actionName);
        Observable<float> GetScalarObservable(int actionId);

        // Context Management - Object Based
        void PushContext(InputContext context);
        bool RemoveContext(InputContext context);
        void PopContext();
        void RefreshActiveContext();

        // Binding Management
        bool RemoveBindingFromContext(InputContext context, Observable<Unit> source);
        bool RemoveBindingFromContext(InputContext context, Observable<Vector2> source);
        bool RemoveBindingFromContext(InputContext context, Observable<float> source);
        bool RemoveBindingFromContext(InputContext context, Observable<bool> source);

        void BlockInput();
        void UnblockInput();

        /// <summary>
        /// Returns true if the left mouse button is currently pressed (polling).
        /// Safe to call even if no mouse is present (returns false).
        /// </summary>
        bool IsLeftMouseButtonPressed { get; }

        /// <summary>
        /// Returns true if the right mouse button is currently pressed (polling).
        /// Safe to call even if no mouse is present (returns false).
        /// </summary>
        bool IsRightMouseButtonPressed { get; }

        /// <summary>
        /// Returns true if the middle mouse button is currently pressed (polling).
        /// Safe to call even if no mouse is present (returns false).
        /// </summary>
        bool IsMiddleMouseButtonPressed { get; }
    }
}
