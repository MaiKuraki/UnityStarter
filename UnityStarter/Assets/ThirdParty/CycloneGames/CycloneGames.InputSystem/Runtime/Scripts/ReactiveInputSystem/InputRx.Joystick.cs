using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public static partial class InputRx
    {
        public static Observable<Vector2> OnJoystickStickChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Joystick.current == null ? default : Joystick.current.stick.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnJoystickStickChanged(Joystick joystick, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(joystick);

            return Observable.EveryValueChanged(
                joystick,
                joystick => joystick.stick.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<float> OnJoystickTriggerChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Joystick.current == null ? default : Joystick.current.trigger.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<float> OnJoystickTriggerChanged(Joystick joystick, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(joystick);
            
            return Observable.EveryValueChanged(
                joystick,
                joystick => joystick.trigger.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }
    }
}
