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
        int PlayerId { get; }
        ReadOnlyReactiveProperty<string> ActiveContextName { get; }
        ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind { get; }
        event Action<string> OnContextChanged;
        event Action<InputPlayerDeviceStatus> OnDeviceStatusChanged;

        /// <summary>
        /// Returns an observable that only emits ActiveDeviceKind changes when the specified context is active (at the top of the stack).
        /// This makes device kind subscriptions behave like context bindings - only the top context responds.
        /// </summary>
        Observable<InputDeviceKind> GetActiveDeviceKindObservableForContext(InputContext context);

        Observable<Vector2> GetVector2Observable(string contextName, string actionMapName, string actionName);
        Observable<Vector2> GetVector2Observable(int actionId);

        Observable<Unit> GetButtonObservable(string contextName, string actionMapName, string actionName);
        Observable<Unit> GetButtonObservable(int actionId);

        /// <summary>
        /// Fires when button is held for configured long-press duration. Returns empty if not configured.
        /// </summary>
        Observable<Unit> GetLongPressObservable(string contextName, string actionMapName, string actionName);
        Observable<Unit> GetLongPressObservable(int actionId);

        /// <summary>
        /// Emits continuous progress (0~1) while holding button. Emits -1 when released before completion.
        /// Returns an empty observable when long press is not configured.
        /// </summary>
        Observable<float> GetLongPressProgressObservable(string contextName, string actionMapName, string actionName);
        Observable<float> GetLongPressProgressObservable(int actionId);

        /// <summary>
        /// Emits true on press start, false on release.
        /// </summary>
        Observable<bool> GetPressStateObservable(string contextName, string actionMapName, string actionName);
        Observable<bool> GetPressStateObservable(int actionId);

        Observable<float> GetScalarObservable(string contextName, string actionMapName, string actionName);
        Observable<float> GetScalarObservable(int actionId);

        // Context Management - Object Based
        void PushContext(InputContext context);

        /// <summary>
        /// Temporarily captures active input with the specified context.
        /// Normal contexts may still be pushed and removed while the capture is active.
        /// Dispose the returned scope to restore the next captured context or the normal stack top.
        /// </summary>
        IDisposable CaptureContext(InputContext context);

        bool RemoveContext(InputContext context);
        void PopContext();
        void RefreshActiveContext();

        /// <summary>
        /// Sets the priority and blocking policy this player applies to an owned context, without
        /// changing the context. A changed policy triggers one context refresh.
        /// </summary>
        /// <returns>false when the context is null or not owned by this player.</returns>
        bool SetContextPolicy(InputContext context, int priority, bool blocksLowerPriority);

        /// <summary>
        /// Removes the policy set by <see cref="SetContextPolicy"/>. Returns false when no override exists.
        /// </summary>
        bool ClearContextPolicy(InputContext context);

        // Binding Management
        bool RemoveBindingFromContext(InputContext context, Observable<Unit> source);
        bool RemoveBindingFromContext(InputContext context, Observable<Vector2> source);
        bool RemoveBindingFromContext(InputContext context, Observable<float> source);
        bool RemoveBindingFromContext(InputContext context, Observable<bool> source);

        /// <summary>
        /// Disables all input until <see cref="UnblockInput"/> has been called the same number of times.
        /// Contexts can still be pushed and removed while input is blocked.
        /// </summary>
        void BlockInput();

        /// <summary>
        /// Creates a scoped input block. Dispose the returned scope to release it.
        /// </summary>
        IDisposable BlockInputScope();

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

        /// <summary>
        /// Overrides a specific device binding path for an action. Only the matching binding is replaced.
        /// Returns true if the old binding was found and overridden.
        /// </summary>

        /// <summary>
        /// Resets a specific action's bindings to their original configuration (removes all overrides).
        /// Returns true if the action was found.
        /// </summary>

        /// <summary>
        /// Resets all actions' bindings to their original configuration.
        /// </summary>
        void ResetAllActionBindings();

        /// <summary>
        /// Returns the current effective binding paths for an action (including any overrides).
        /// Returns an empty array if the action is not found.
        /// </summary>

        /// <summary>
        /// Context-qualified overloads for configurations that reuse an action map and action name.
        /// </summary>
        bool RebindAction(string contextName, string actionMapName, string actionName, string oldBinding, string newBinding);
        bool ResetActionBinding(string contextName, string actionMapName, string actionName);
        string[] GetActionBindings(string contextName, string actionMapName, string actionName);

        /// <summary>
        /// Exports or imports the module-owned, versioned stable binding override JSON format.
        /// Records use context/map/action identity plus binding ordinal and original binding metadata.
        /// </summary>
        string ExportBindingOverridesJson();
        bool TryExportBindingOverridesJson(out string json);
        bool ImportBindingOverridesJson(string json, bool removeExisting = true);

        /// <summary>
        /// Reads an advanced value synchronously on the Unity main thread without exposing CallbackContext.
        /// </summary>
        bool TryReadValue<TValue>(string contextName, string actionMapName, string actionName, out TValue value)
            where TValue : struct;
        bool TryReadValue<TValue>(int actionId, out TValue value)
            where TValue : struct;

        /// <summary>
        /// Emits when two button actions are pressed within the specified time window of each other.
        /// Resets when either button is released. Returns an empty observable if either action is not
        /// configured as a Button.
        /// </summary>
        Observable<Unit> GetChordObservable(int actionId1, int actionId2, float windowMs = 300f);
    }
}
