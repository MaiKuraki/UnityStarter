using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using R3;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace CycloneGames.InputSystem.Runtime
{
    public static partial class InputRx
    {
        public static Observable<Unit> OnTouchDown(int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentOutOfRangeException(touchId < 0);

            return OnAfterUpdate(cancellationToken)
                .Where(touchId, (_, touchId) =>
                {
                    var touchscreen = Touchscreen.current;
                    return IsTouchPressedThisFrame(touchscreen, touchId);
                });
        }

        public static Observable<Unit> OnTouchDown(Touchscreen touchscreen, int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(touchscreen);
            Error.ArgumentOutOfRangeException(touchId < 0);

            return OnAfterUpdate(cancellationToken)
                .Where((touchscreen, touchId), (_, state) =>
                    IsTouchPressedThisFrame(state.touchscreen, state.touchId)
                );
        }

        public static Observable<Unit> OnTouch(int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentOutOfRangeException(touchId < 0);

            return OnAfterUpdate(cancellationToken)
                .Where(touchId, (_, touchId) =>
                {
                    var touchscreen = Touchscreen.current;
                    return IsTouchPressed(touchscreen, touchId);
                });
        }

        public static Observable<Unit> OnTouch(Touchscreen touchscreen, int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(touchscreen);
            Error.ArgumentOutOfRangeException(touchId < 0);

            return OnAfterUpdate(cancellationToken)
                .Where((touchscreen, touchId), (_, state) =>
                    IsTouchPressed(state.touchscreen, state.touchId)
                );
        }

        public static Observable<Unit> OnTouchUp(int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentOutOfRangeException(touchId < 0);

            return OnAfterUpdate(cancellationToken)
                .Where(touchId, (_, touchId) =>
                {
                    var touchscreen = Touchscreen.current;
                    return IsTouchReleasedThisFrame(touchscreen, touchId);
                });
        }

        public static Observable<Unit> OnTouchUp(Touchscreen touchscreen, int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(touchscreen);
            Error.ArgumentOutOfRangeException(touchId < 0);

            return OnAfterUpdate(cancellationToken)
                .Where((touchscreen, touchId), (_, state) =>
                    IsTouchReleasedThisFrame(state.touchscreen, state.touchId)
                );
        }

        public static Observable<TouchPhase> OnTouchPhaseChanged(int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentOutOfRangeException(touchId < 0);

            return Observable.EveryValueChanged<object, TouchPhase>(null,
                _ =>
                {
                    var touchscreen = Touchscreen.current;
                    if (!HasTouch(touchscreen, touchId))
                    {
                        return default;
                    }

                    return touchscreen.touches[touchId].phase.value;
                },
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<TouchPhase> OnTouchPhaseChanged(Touchscreen touchscreen, int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(touchscreen);
            Error.ArgumentOutOfRangeException(touchId < 0);

            return Observable.EveryValueChanged(touchscreen,
                touchscreen =>
                {
                    if (!HasTouch(touchscreen, touchId))
                    {
                        return default;
                    }

                    return touchscreen.touches[touchId].phase.value;
                },
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnTouchPositionChanged(int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentOutOfRangeException(touchId < 0);

            return Observable.EveryValueChanged<object, Vector2>(null,
                _ =>
                {
                    var touchscreen = Touchscreen.current;
                    if (!HasTouch(touchscreen, touchId))
                    {
                        return default;
                    }

                    return touchscreen.touches[touchId].position.value;
                },
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnTouchPositionChanged(Touchscreen touchscreen, int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(touchscreen);
            Error.ArgumentOutOfRangeException(touchId < 0);

            return Observable.EveryValueChanged(touchscreen,
                touchscreen =>
                {
                    if (!HasTouch(touchscreen, touchId))
                    {
                        return default;
                    }

                    return touchscreen.touches[touchId].position.value;
                },
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnTouchDeltaChanged(int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentOutOfRangeException(touchId < 0);

            return Observable.EveryValueChanged<object, Vector2>(null,
                _ =>
                {
                    var touchscreen = Touchscreen.current;
                    if (!HasTouch(touchscreen, touchId))
                    {
                        return default;
                    }

                    return touchscreen.touches[touchId].delta.value;
                },
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        public static Observable<Vector2> OnTouchDeltaChanged(Touchscreen touchscreen, int touchId, CancellationToken cancellationToken = default)
        {
            Error.ArgumentNullException(touchscreen);
            Error.ArgumentOutOfRangeException(touchId < 0);
            
            return Observable.EveryValueChanged(touchscreen,
                touchscreen =>
                {
                    if (!HasTouch(touchscreen, touchId))
                    {
                        return default;
                    }

                    return touchscreen.touches[touchId].delta.value;
                },
                InputSystemFrameProvider.AfterUpdate,
                cancellationToken
            );
        }

        static bool HasTouch(Touchscreen touchscreen, int touchId)
        {
            return touchscreen != null && touchId < touchscreen.touches.Count;
        }

        static bool IsTouchPressedThisFrame(Touchscreen touchscreen, int touchId)
        {
            return HasTouch(touchscreen, touchId) && touchscreen.touches[touchId].press.wasPressedThisFrame;
        }

        static bool IsTouchPressed(Touchscreen touchscreen, int touchId)
        {
            return HasTouch(touchscreen, touchId) && touchscreen.touches[touchId].press.isPressed;
        }

        static bool IsTouchReleasedThisFrame(Touchscreen touchscreen, int touchId)
        {
            return HasTouch(touchscreen, touchId) && touchscreen.touches[touchId].press.wasReleasedThisFrame;
        }
    }
}
