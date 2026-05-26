using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public static partial class InputRx
    {
        static readonly object StaticObject = new();

        public static Observable<MouseButton> OnAnyMouseButtonDown(Mouse mouse, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);
            return new AnyMouseButtonDown(mouse, cancellationToken);
        }

        public static Observable<MouseButton> OnAnyMouseButtonDown(CancellationToken cancellationToken = default)
        {
            return new AnyMouseButtonDown(null, cancellationToken);
        }

        public static Observable<MouseButton> OnAnyMouseButton(Mouse mouse, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);
            return new AnyMouseButton(mouse, cancellationToken);
        }

        public static Observable<MouseButton> OnAnyMouseButton(CancellationToken cancellationToken = default)
        {
            return new AnyMouseButton(null, cancellationToken);
        }

        public static Observable<MouseButton> OnAnyMouseButtonUp(Mouse mouse, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);
            return new AnyMouseButtonUp(mouse, cancellationToken);
        }

        public static Observable<MouseButton> OnAnyMouseButtonUp(CancellationToken cancellationToken = default)
        {
            return new AnyMouseButtonUp(null, cancellationToken);
        }

        public static Observable<Unit> OnMouseButtonDown(Mouse mouse, MouseButton mouseButton, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);

            return OnAfterUpdate(cancellationToken)
                .Where((mouse, mouseButton), (_, state) =>
                {
                    var control = InputControlHelper.GetMouseButtonControl(state.mouse, state.mouseButton);
                    return control != null && control.wasPressedThisFrame;
                });
        }

        public static Observable<Unit> OnMouseButtonDown(MouseButton mouseButton, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(mouseButton, (_, mouseButton) =>
                {
                    var mouse = Mouse.current;
                    if (mouse == null)
                    {
                        return false;
                    }

                    var control = InputControlHelper.GetMouseButtonControl(mouse, mouseButton);
                    return control != null && control.wasPressedThisFrame;
                });
        }

        public static Observable<Unit> OnMouseButton(Mouse mouse, MouseButton mouseButton, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);

            return OnAfterUpdate(cancellationToken)
                .Where((mouse, mouseButton), (_, state) =>
                {
                    var control = InputControlHelper.GetMouseButtonControl(state.mouse, state.mouseButton);
                    return control != null && control.isPressed;
                });
        }

        public static Observable<Unit> OnMouseButton(MouseButton mouseButton, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(mouseButton, (_, mouseButton) =>
                {
                    var mouse = Mouse.current;
                    if (mouse == null)
                    {
                        return false;
                    }

                    var control = InputControlHelper.GetMouseButtonControl(mouse, mouseButton);
                    return control != null && control.isPressed;
                });
        }

        public static Observable<Unit> OnMouseButtonUp(Mouse mouse, MouseButton mouseButton, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);

            return OnAfterUpdate(cancellationToken)
                .Where((mouse, mouseButton), (_, state) =>
                {
                    var control = InputControlHelper.GetMouseButtonControl(state.mouse, state.mouseButton);
                    return control != null && control.wasReleasedThisFrame;
                });
        }

        public static Observable<Unit> OnMouseButtonUp(MouseButton mouseButton, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(mouseButton, (_, mouseButton) =>
                {
                    var mouse = Mouse.current;
                    if (mouse == null)
                    {
                        return false;
                    }

                    var control = InputControlHelper.GetMouseButtonControl(mouse, mouseButton);
                    return control != null && control.wasReleasedThisFrame;
                });
        }
        
        public static Observable<Vector2> OnMousePositionChanged(Mouse mouse, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);

            return Observable.EveryValueChanged(mouse, mouse => mouse.position.value, InputSystemFrameProvider.AfterUpdate, cancellationToken);
        }

        public static Observable<Vector2> OnMousePositionChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Mouse.current == null ? default : Mouse.current.position.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnMouseDeltaChanged(Mouse mouse, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);

            return Observable.EveryValueChanged(mouse, mouse => mouse.delta.value, InputSystemFrameProvider.AfterUpdate, cancellationToken);
        }

        public static Observable<Vector2> OnMouseDeltaChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Mouse.current == null ? default : Mouse.current.delta.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnMouseScrollChanged(Mouse mouse, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(mouse);
            
            return Observable.EveryValueChanged(mouse, mouse => mouse.scroll.value, InputSystemFrameProvider.AfterUpdate, cancellationToken);
        }

        public static Observable<Vector2> OnMouseScrollChanged(CancellationToken cancellationToken = default)
        {
            return Observable.EveryValueChanged(
                StaticObject,
                _ => Mouse.current == null ? default : Mouse.current.scroll.value,
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }
    }
}
