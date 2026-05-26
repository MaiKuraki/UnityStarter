using System.Threading;
using UnityEngine.InputSystem;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public static class InputActionMapExtensions
    {
        public static Observable<InputAction.CallbackContext> OnActionTriggered(this InputActionMap inputActions, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(inputActions);
            return Observable.FromEvent<InputAction.CallbackContext>(
                h => inputActions.actionTriggered += h,
                h => inputActions.actionTriggered -= h,
                cancellationToken
            );
        }

        public static Observable<InputAction.CallbackContext> OnActionStarted(this InputActionMap inputActions, CancellationToken cancellationToken = default)
        {
            return OnActionTriggered(inputActions, cancellationToken)
                .Where(static context => context.started);
        }

        public static Observable<InputAction.CallbackContext> OnActionPerformed(this InputActionMap inputActions, CancellationToken cancellationToken = default)
        {
            return OnActionTriggered(inputActions, cancellationToken)
                .Where(static context => context.performed);
        }

        public static Observable<InputAction.CallbackContext> OnActionCanceled(this InputActionMap inputActions, CancellationToken cancellationToken = default)
        {
            return OnActionTriggered(inputActions, cancellationToken)
                .Where(static context => context.canceled);
        }
    }
}
