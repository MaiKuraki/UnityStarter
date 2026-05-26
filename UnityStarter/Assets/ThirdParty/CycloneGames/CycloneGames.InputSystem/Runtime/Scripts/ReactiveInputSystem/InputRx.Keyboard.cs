using System.Threading;
using UnityEngine.InputSystem;
using R3;
using UnityEngine.InputSystem.LowLevel;

namespace CycloneGames.InputSystem.Runtime
{
    public static partial class InputRx
    {
        public static Observable<char> OnTextInput(Keyboard keyboard, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(keyboard);

            return Observable.FromEvent<char>(
                h => keyboard.onTextInput += h,
                h => keyboard.onTextInput -= h,
                cancellationToken
            );
        }

        public static Observable<char> OnTextInput(CancellationToken cancellationToken = default)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Observable.Empty<char>();
            }

            return Observable.FromEvent<char>(
                h => keyboard.onTextInput += h,
                h => keyboard.onTextInput -= h,
                cancellationToken
            );
        }

        public static Observable<IMECompositionString> OnIMECompositionChange(Keyboard keyboard, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(keyboard);

            return Observable.FromEvent<IMECompositionString>(
                h => keyboard.onIMECompositionChange += h,
                h => keyboard.onIMECompositionChange -= h,
                cancellationToken
            );
        }

        public static Observable<IMECompositionString> OnIMECompositionChange(CancellationToken cancellationToken = default)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Observable.Empty<IMECompositionString>();
            }

            return Observable.FromEvent<IMECompositionString>(
                h => keyboard.onIMECompositionChange += h,
                h => keyboard.onIMECompositionChange -= h,
                cancellationToken
            );
        }

        public static Observable<Key> OnAnyKeyDown(Keyboard keyboard, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(keyboard);
            return new AnyKeyDown(keyboard, cancellationToken);
        }

        public static Observable<Key> OnAnyKeyDown(CancellationToken cancellationToken = default)
        {
            return new AnyKeyDown(null, cancellationToken);
        }

        public static Observable<Key> OnAnyKey(Keyboard keyboard, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(keyboard);
            return new AnyKey(keyboard, cancellationToken);
        }

        public static Observable<Key> OnAnyKey(CancellationToken cancellationToken = default)
        {
            return new AnyKey(null, cancellationToken);
        }


        public static Observable<Key> OnAnyKeyUp(Keyboard keyboard, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(keyboard);
            return new AnyKeyUp(keyboard, cancellationToken);
        }

        public static Observable<Key> OnAnyKeyUp(CancellationToken cancellationToken = default)
        {
            return new AnyKeyUp(null, cancellationToken);
        }

        public static Observable<Unit> OnKeyDown(Keyboard keyboard, Key key, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(keyboard);

            return OnAfterUpdate(cancellationToken)
                .Where((keyboard, key), (_, state) => IsKeyPressedThisFrame(state.keyboard, state.key));
        }

        public static Observable<Unit> OnKeyDown(Key key, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(key, (_, key) =>
                {
                    var keyboard = Keyboard.current;
                    return IsKeyPressedThisFrame(keyboard, key);
                });
        }

        public static Observable<Unit> OnKey(Keyboard keyboard, Key key, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(keyboard);

            return OnAfterUpdate(cancellationToken)
                .Where((keyboard, key), (_, state) => IsKeyPressed(state.keyboard, state.key));
        }

        public static Observable<Unit> OnKey(Key key, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(key, (_, key) =>
                {
                    var keyboard = Keyboard.current;
                    return IsKeyPressed(keyboard, key);
                });
        }

        public static Observable<Unit> OnKeyUp(Keyboard keyboard, Key key, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(keyboard);
            
            return OnAfterUpdate(cancellationToken)
                .Where((keyboard, key), (_, state) => IsKeyReleasedThisFrame(state.keyboard, state.key));
        }

        public static Observable<Unit> OnKeyUp(Key key, CancellationToken cancellationToken = default)
        {
            return OnAfterUpdate(cancellationToken)
                .Where(key, (_, key) =>
                {
                    var keyboard = Keyboard.current;
                    return IsKeyReleasedThisFrame(keyboard, key);
                });
        }

        static bool HasKey(Keyboard keyboard, Key key)
        {
            var index = (int)key - 1;
            return keyboard != null && index >= 0 && index < keyboard.allKeys.Count;
        }

        static bool IsKeyPressedThisFrame(Keyboard keyboard, Key key)
        {
            return HasKey(keyboard, key) && keyboard[key].wasPressedThisFrame;
        }

        static bool IsKeyPressed(Keyboard keyboard, Key key)
        {
            return HasKey(keyboard, key) && keyboard[key].isPressed;
        }

        static bool IsKeyReleasedThisFrame(Keyboard keyboard, Key key)
        {
            return HasKey(keyboard, key) && keyboard[key].wasReleasedThisFrame;
        }
    }
}
