using System;
using System.Threading;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public static partial class InputRx
    {
        public static Observable<InputControl> OnAnyButtonPress()
        {
            return UnityEngine.InputSystem.InputSystem.onAnyButtonPress.ToObservable();
        }

        public static Observable<InputEventPtr> OnEvent()
        {
            return UnityEngine.InputSystem.InputSystem.onEvent.ToObservable();
        }

        public static Observable<Unit> OnBeforeUpdate(CancellationToken cancellationToken = default)
        {
            return Observable.EveryUpdate(InputSystemFrameProvider.BeforeUpdate, cancellationToken);
        }

        public static Observable<Unit> OnAfterUpdate(CancellationToken cancellationToken = default)
        {
            return Observable.EveryUpdate(InputSystemFrameProvider.AfterUpdate, cancellationToken);
        }

        public static Observable<(object, InputActionChange)> OnActionChange(CancellationToken cancellationToken = default)
        {
            return Observable.FromEvent<Action<object, InputActionChange>, (object, InputActionChange)>(
                h => (x, y) => h((x, y)),
                h => UnityEngine.InputSystem.InputSystem.onActionChange += h,
                h => UnityEngine.InputSystem.InputSystem.onActionChange -= h,
                cancellationToken
            );
        }

        public static Observable<(InputDevice, InputDeviceChange)> OnDeviceChange(CancellationToken cancellationToken = default)
        {
            return Observable.FromEvent<Action<InputDevice, InputDeviceChange>, (InputDevice, InputDeviceChange)>(
                h => (x, y) => h((x, y)),
                h => UnityEngine.InputSystem.InputSystem.onDeviceChange += h,
                h => UnityEngine.InputSystem.InputSystem.onDeviceChange -= h,
                cancellationToken
            );
        }

        public static Observable<InputDevice> OnDeviceAdded(CancellationToken cancellationToken = default)
        {
            return OnDeviceChange(InputDeviceChange.Added, cancellationToken);
        }

        public static Observable<InputDevice> OnDeviceRemoved(CancellationToken cancellationToken = default)
        {
            return OnDeviceChange(InputDeviceChange.Removed, cancellationToken);
        }

        public static Observable<InputDevice> OnDeviceDisconnected(CancellationToken cancellationToken = default)
        {
            return OnDeviceChange(InputDeviceChange.Disconnected, cancellationToken);
        }

        public static Observable<InputDevice> OnDeviceReconnected(CancellationToken cancellationToken = default)
        {
            return OnDeviceChange(InputDeviceChange.Reconnected, cancellationToken);
        }

        static Observable<InputDevice> OnDeviceChange(InputDeviceChange deviceChange, CancellationToken cancellationToken)
        {
            return OnDeviceChange(cancellationToken)
                .Where(deviceChange, static (state, deviceChange) => state.Item2 == deviceChange)
                .Select(static state => state.Item1);
        }
    }
}
