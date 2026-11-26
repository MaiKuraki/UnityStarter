using System;
using R3;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Read-only surface of a player: action streams, synchronous reads, buffered press events and the device kind
    /// that currently drives them. Presentation and gameplay code can depend on this interface alone.
    /// </summary>
    public interface IInputActionReader
    {
        /// <summary>Device kind driving the most recent action, or <see cref="InputDeviceKind.Unknown"/>.</summary>
        ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind { get; }

        /// <summary>Raised when a paired device connects, disconnects or changes layout.</summary>
        event Action<InputPlayerDeviceStatus> OnDeviceStatusChanged;

        /// <summary>Emits the active device kind for the duration of a context.</summary>
        Observable<InputDeviceKind> GetActiveDeviceKindObservableForContext(InputContext context);

        /// <summary>Emits the action value. A <c>Vector2</c> action emits zero on cancel.</summary>
        Observable<Vector2> GetVector2Observable(string contextName, string actionMapName, string actionName);

        /// <summary>Context-qualified overload for configurations that reuse an action name.</summary>
        Observable<Vector2> GetVector2Observable(int actionId);

        /// <summary>Emits once per performed event.</summary>
        Observable<Unit> GetButtonObservable(string contextName, string actionMapName, string actionName);

        /// <summary>Context-qualified overload for configurations that reuse an action name.</summary>
        Observable<Unit> GetButtonObservable(int actionId);

        /// <summary>Emits once when the hold reaches <c>longPressMs</c>.</summary>
        Observable<Unit> GetLongPressObservable(string contextName, string actionMapName, string actionName);

        /// <summary>Context-qualified overload for configurations that reuse an action name.</summary>
        Observable<Unit> GetLongPressObservable(int actionId);

        /// <summary>Emits hold progress in <c>0..1</c>, or <c>-1</c> when released early.</summary>
        Observable<float> GetLongPressProgressObservable(string contextName, string actionMapName, string actionName);

        /// <summary>Context-qualified overload for configurations that reuse an action name.</summary>
        Observable<float> GetLongPressProgressObservable(int actionId);

        /// <summary>Emits the current pressed state on subscribe, then on every change.</summary>
        Observable<bool> GetPressStateObservable(string contextName, string actionMapName, string actionName);

        /// <summary>Context-qualified overload for configurations that reuse an action name.</summary>
        Observable<bool> GetPressStateObservable(int actionId);

        /// <summary>Emits the action value. A <c>Float</c> action emits zero on cancel.</summary>
        Observable<float> GetScalarObservable(string contextName, string actionMapName, string actionName);

        /// <summary>Context-qualified overload for configurations that reuse an action name.</summary>
        Observable<float> GetScalarObservable(int actionId);

        /// <summary>Reads the current value on the Unity main thread without exposing <c>CallbackContext</c>.</summary>
        bool TryReadValue<TValue>(string contextName, string actionMapName, string actionName, out TValue value)
            where TValue : struct;

        /// <summary>Reads the current value by deterministic action ID.</summary>
        bool TryReadValue<TValue>(int actionId, out TValue value)
            where TValue : struct;

        /// <summary>Emits when both buttons are pressed inside <paramref name="windowMs"/>.</summary>
        Observable<Unit> GetChordObservable(int actionId1, int actionId2, float windowMs = 300f);

        /// <summary>Buffered press events. Cleared on context and block changes.</summary>
        InputActionBuffer ActionBuffer { get; }

        /// <summary>Takes a press event recorded within the last <paramref name="windowFrames"/> frames.</summary>
        bool TryConsumeBuffered(int actionId, int windowFrames);

        /// <summary>Reads a buffered press without taking ownership.</summary>
        bool TryPeekBuffered(int actionId, int windowFrames);

        /// <summary>Left mouse button state of the paired pointer device.</summary>
        bool IsLeftMouseButtonPressed { get; }

        /// <summary>Right mouse button state of the paired pointer device.</summary>
        bool IsRightMouseButtonPressed { get; }

        /// <summary>Middle mouse button state of the paired pointer device.</summary>
        bool IsMiddleMouseButtonPressed { get; }
    }
}
