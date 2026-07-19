using System;
using System.Threading;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    internal sealed class AnyGamepadButtonDown : AnyGamepadButtonObservableBase
    {
        public AnyGamepadButtonDown(Gamepad gamepad, CancellationToken cancellationToken) : base(gamepad, cancellationToken) { }

        protected override bool CheckGamepad(Gamepad gamepad, GamepadButton gamepadButton)
        {
            return gamepad[gamepadButton].wasPressedThisFrame;
        }
    }

    internal sealed class AnyGamepadButton : AnyGamepadButtonObservableBase
    {
        public AnyGamepadButton(Gamepad gamepad, CancellationToken cancellationToken) : base(gamepad, cancellationToken) { }

        protected override bool CheckGamepad(Gamepad gamepad, GamepadButton gamepadButton)
        {
            return gamepad[gamepadButton].isPressed;
        }
    }

    internal sealed class AnyGamepadButtonUp : AnyGamepadButtonObservableBase
    {
        public AnyGamepadButtonUp(Gamepad gamepad, CancellationToken cancellationToken) : base(gamepad, cancellationToken) { }

        protected override bool CheckGamepad(Gamepad gamepad, GamepadButton gamepadButton)
        {
            return gamepad[gamepadButton].wasReleasedThisFrame;
        }
    }

    internal abstract class AnyGamepadButtonObservableBase : Observable<GamepadButton>
    {
        public AnyGamepadButtonObservableBase(Gamepad gamepad, CancellationToken cancellationToken)
        {
            this.gamepad = gamepad;
            this.cancellationToken = cancellationToken;
        }

        readonly Gamepad gamepad;
        readonly CancellationToken cancellationToken;

        protected abstract bool CheckGamepad(Gamepad gamepad, GamepadButton gamepadButton);

        protected override IDisposable SubscribeCore(Observer<GamepadButton> observer)
        {
            if (observer.IsDisposed)
            {
                return Disposable.Empty;
            }

            var runner = new FrameRunnerWorkItem(this, observer, gamepad, cancellationToken);
            InputSystemFrameProvider.AfterUpdate.Register(runner);
            return runner;
        }

        sealed class FrameRunnerWorkItem : CancellableFrameRunnerWorkItemBase<GamepadButton>
        {
            // Canonical physical controls only. Platform aliases (A/Cross/South, etc.) share enum values
            // and must not produce duplicate emissions for one control.
            static readonly GamepadButton[] AllGamepadButtons =
            {
                GamepadButton.DpadUp,
                GamepadButton.DpadDown,
                GamepadButton.DpadLeft,
                GamepadButton.DpadRight,
                GamepadButton.North,
                GamepadButton.East,
                GamepadButton.South,
                GamepadButton.West,
                GamepadButton.LeftStick,
                GamepadButton.RightStick,
                GamepadButton.LeftShoulder,
                GamepadButton.RightShoulder,
                GamepadButton.Start,
                GamepadButton.Select,
                GamepadButton.LeftTrigger,
                GamepadButton.RightTrigger
            };

            readonly AnyGamepadButtonObservableBase source;
            readonly Gamepad gamepad;
            readonly GamepadButton[] buffer;
            int bufferCount;

            public FrameRunnerWorkItem(AnyGamepadButtonObservableBase source, Observer<GamepadButton> observer, Gamepad gamepad, CancellationToken cancellationToken) : base(observer, cancellationToken)
            {
                this.source = source;
                this.gamepad = gamepad;
                this.buffer = new GamepadButton[AllGamepadButtons.Length];
            }

            protected override bool MoveNextCore(long _)
            {
                bufferCount = 0;

                var gamepad = this.gamepad ?? Gamepad.current;
                if (gamepad == null)
                {
                    return true;
                }

                for (int i = 0; i < AllGamepadButtons.Length; i++)
                {
                    var button = AllGamepadButtons[i];
                    if (source.CheckGamepad(gamepad, button))
                    {
                        buffer[bufferCount] = button;
                        bufferCount++;
                    }
                }

                for (int i = 0; i < bufferCount; i++)
                {
                    PublishOnNext(buffer[i]);
                }

                return true;
            }
        }
    }
}
