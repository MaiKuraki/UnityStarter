using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public static partial class InputRx
    {
        public static Observable<GamepadButton> OnAnyGamepadButtonDown(Gamepad gamepad, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);
            return new AnyGamepadButtonDown(gamepad, cancellationToken);
        }

        public static Observable<GamepadButton> OnAnyGamepadButtonDown(CancellationToken cancellationToken = default)
        {
            return new AnyGamepadButtonDown(null, cancellationToken);
        }

        public static Observable<GamepadButton> OnAnyGamepadButton(Gamepad gamepad, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);
            return new AnyGamepadButton(gamepad, cancellationToken);
        }

        public static Observable<GamepadButton> OnAnyGamepadButton(CancellationToken cancellationToken = default)
        {
            return new AnyGamepadButton(null, cancellationToken);
        }

        public static Observable<GamepadButton> OnAnyGamepadButtonUp(Gamepad gamepad, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);
            return new AnyGamepadButtonUp(gamepad, cancellationToken);
        }

        public static Observable<GamepadButton> OnAnyGamepadButtonUp(CancellationToken cancellationToken = default)
        {
            return new AnyGamepadButtonUp(null, cancellationToken);
        }

        public static Observable<Unit> OnGamepadButtonDown(GamepadButton gamepadButton, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(_ => Gamepad.current != null && Gamepad.current[gamepadButton].wasPressedThisFrame);
        }

        public static Observable<Unit> OnGamepadButtonDown(Gamepad gamepad, GamepadButton gamepadButton, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);

            return OnAfterUpdate(cancellationToken)
                .Where(gamepad, (_, gamepad) => gamepad[gamepadButton].wasPressedThisFrame);
        }

        public static Observable<Unit> OnGamepadButton(GamepadButton gamepadButton, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(_ => Gamepad.current != null && Gamepad.current[gamepadButton].isPressed);
        }

        public static Observable<Unit> OnGamepadButton(Gamepad gamepad, GamepadButton gamepadButton, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);

            return OnAfterUpdate(cancellationToken)
                .Where(gamepad, (_, gamepad) => gamepad[gamepadButton].isPressed);
        }

        public static Observable<Unit> OnGamepadButtonUp(GamepadButton gamepadButton, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(_ => Gamepad.current != null && Gamepad.current[gamepadButton].wasReleasedThisFrame);
        }

        public static Observable<Unit> OnGamepadButtonUp(Gamepad gamepad, GamepadButton gamepadButton, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);

            return OnAfterUpdate(cancellationToken)
                .Where(gamepad, (_, gamepad) => gamepad[gamepadButton].wasReleasedThisFrame);
        }

        public static Observable<Vector2> OnGamepadLeftStickChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Gamepad.current == null ? default : Gamepad.current.leftStick.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnGamepadLeftStickChanged(Gamepad gamepad, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);

            return Observable.EveryValueChanged(
                gamepad,
                gamepad => gamepad.leftStick.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnGamepadRightStickChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Gamepad.current == null ? default : Gamepad.current.rightStick.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnGamepadRightStickChanged(Gamepad gamepad, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);

            return Observable.EveryValueChanged(
                gamepad,
                gamepad => gamepad.rightStick.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<float> OnGamepadLeftTriggerChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Gamepad.current == null ? default : Gamepad.current.leftTrigger.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<float> OnGamepadLeftTriggerChanged(Gamepad gamepad, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);

            return Observable.EveryValueChanged(
                gamepad,
                gamepad => gamepad.leftTrigger.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<float> OnGamepadRightTriggerChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Gamepad.current == null ? default : Gamepad.current.rightTrigger.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<float> OnGamepadRightTriggerChanged(Gamepad gamepad, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);

            return Observable.EveryValueChanged(
                gamepad,
                gamepad => gamepad.rightTrigger.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnGamepadDpadChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Gamepad.current == null ? default : Gamepad.current.dpad.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnGamepadDpadChanged(Gamepad gamepad, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(gamepad);

            return Observable.EveryValueChanged(
                gamepad,
                gamepad => gamepad.dpad.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

    }
}
