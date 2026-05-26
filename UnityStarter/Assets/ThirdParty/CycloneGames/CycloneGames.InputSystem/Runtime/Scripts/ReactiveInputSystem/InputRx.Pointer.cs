using System.Threading;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CycloneGames.InputSystem.Runtime
{
    public static partial class InputRx
    {
        public static Observable<Unit> OnPointerDown(Pointer pointer, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(pointer);

            return OnAfterUpdate(cancellationToken)
                .Where(pointer, (_, pointer) => pointer.press.wasPressedThisFrame);
        }

        public static Observable<Unit> OnPointerDown(CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(_ => Pointer.current != null && Pointer.current.press.wasPressedThisFrame);
        }

        public static Observable<Unit> OnPointer(Pointer pointer, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(pointer);

            return OnAfterUpdate(cancellationToken)
                .Where(pointer, (_, pointer) => pointer.press.isPressed);
        }

        public static Observable<Unit> OnPointer(CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(_ => Pointer.current != null && Pointer.current.press.isPressed);
        }

        public static Observable<Unit> OnPointerUp(Pointer pointer, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(pointer);

            return OnAfterUpdate(cancellationToken)
                .Where(pointer, (_, pointer) => pointer.press.wasReleasedThisFrame);
        }

        public static Observable<Unit> OnPointerUp(CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(_ => Pointer.current != null && Pointer.current.press.wasReleasedThisFrame);
        }

        public static Observable<Vector2> OnPointerPositionChanged(Pointer pointer, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(pointer);

            return Observable.EveryValueChanged(pointer, pointer => pointer.position.value, InputSystemFrameProvider.AfterUpdate, cancellationToken);
        }

        public static Observable<Vector2> OnPointerPositionChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Pointer.current == null ? default : Pointer.current.position.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnPointerDeltaChanged(Pointer pointer, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(pointer);

            return Observable.EveryValueChanged(pointer, pointer => pointer.delta.value, InputSystemFrameProvider.AfterUpdate, cancellationToken);
        }

        public static Observable<Vector2> OnPointerDeltaChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Pointer.current == null ? default : Pointer.current.delta.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }
    }
}
