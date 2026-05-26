using System.Threading;
using UnityEngine.InputSystem;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public static class PlayerInputExtensions
    {
        public static Observable<InputAction.CallbackContext> OnActionTriggeredAsObservable(this PlayerInput playerInput, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(playerInput);

            return Observable.FromEvent<InputAction.CallbackContext>(
                h => playerInput.onActionTriggered += h,
                h => playerInput.onActionTriggered -= h,
                cancellationToken
            );
        }

        public static Observable<PlayerInput> OnControlsChangedAsObservable(this PlayerInput playerInput, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(playerInput);

            return Observable.FromEvent<PlayerInput>(
                h => playerInput.onControlsChanged += h,
                h => playerInput.onControlsChanged -= h,
                cancellationToken
            );
        }

        public static Observable<PlayerInput> OnDeviceLostAsObservable(this PlayerInput playerInput, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(playerInput);

            return Observable.FromEvent<PlayerInput>(
                h => playerInput.onDeviceLost += h,
                h => playerInput.onDeviceLost -= h,
                cancellationToken
            );
        }

        public static Observable<PlayerInput> OnDeviceRegainedAsObservable(this PlayerInput playerInput, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(playerInput);

            return Observable.FromEvent<PlayerInput>(
                h => playerInput.onDeviceRegained += h,
                h => playerInput.onDeviceRegained -= h,
                cancellationToken
            );
        }
    }
}
