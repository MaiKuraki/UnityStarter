using System.Threading;
using UnityEngine.InputSystem;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public static class InputActionExtensions
    {
        public static Observable<InputAction.CallbackContext> PerformedAsObservable(this InputAction inputAction, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(inputAction);
            return Observable.FromEvent<InputAction.CallbackContext>(
                h => inputAction.performed += h,
                h => inputAction.performed -= h,
                cancellationToken
            );
        }

        public static Observable<InputAction.CallbackContext> CanceledAsObservable(this InputAction inputAction, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(inputAction);
            return Observable.FromEvent<InputAction.CallbackContext>(
                h => inputAction.canceled += h,
                h => inputAction.canceled -= h,
                cancellationToken
            );
        }

        public static Observable<InputAction.CallbackContext> StartedAsObservable(this InputAction inputAction, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(inputAction);
            return Observable.FromEvent<InputAction.CallbackContext>(
                h => inputAction.started += h,
                h => inputAction.started -= h,
                cancellationToken
            );
        }
    }
}