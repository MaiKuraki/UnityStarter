using System;
using System.Threading;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    internal sealed class AnyMouseButtonDown : AnyMouseButtonObservableBase
    {
        public AnyMouseButtonDown(Mouse mouse, CancellationToken cancellationToken) : base(mouse, cancellationToken) { }

        protected override bool CheckMouse(Mouse mouse, MouseButton mouseButton)
        {
            var control = InputControlHelper.GetMouseButtonControl(mouse, mouseButton);
            return control != null && control.wasPressedThisFrame;
        }
    }

    internal sealed class AnyMouseButton : AnyMouseButtonObservableBase
    {
        public AnyMouseButton(Mouse mouse, CancellationToken cancellationToken) : base(mouse, cancellationToken) { }

        protected override bool CheckMouse(Mouse mouse, MouseButton mouseButton)
        {
            var control = InputControlHelper.GetMouseButtonControl(mouse, mouseButton);
            return control != null && control.isPressed;
        }
    }

    internal sealed class AnyMouseButtonUp : AnyMouseButtonObservableBase
    {
        public AnyMouseButtonUp(Mouse mouse, CancellationToken cancellationToken) : base(mouse, cancellationToken) { }

        protected override bool CheckMouse(Mouse mouse, MouseButton mouseButton)
        {
            var control = InputControlHelper.GetMouseButtonControl(mouse, mouseButton);
            return control != null && control.wasReleasedThisFrame;
        }
    }

    internal abstract class AnyMouseButtonObservableBase : Observable<MouseButton>
    {
        public AnyMouseButtonObservableBase(Mouse mouse, CancellationToken cancellationToken)
        {
            this.mouse = mouse;
            this.cancellationToken = cancellationToken;
        }

        readonly Mouse mouse;
        readonly CancellationToken cancellationToken;

        protected abstract bool CheckMouse(Mouse mouse, MouseButton mouseButton);

        protected override IDisposable SubscribeCore(Observer<MouseButton> observer)
        {
            if (observer.IsDisposed)
            {
                return Disposable.Empty;
            }

            var runner = new FrameRunnerWorkItem(this, observer, mouse, cancellationToken);
            InputSystemFrameProvider.AfterUpdate.Register(runner);
            return runner;
        }

        sealed class FrameRunnerWorkItem : CancellableFrameRunnerWorkItemBase<MouseButton>
        {
            static readonly MouseButton[] AllMouseButtons = (MouseButton[])Enum.GetValues(typeof(MouseButton));

            readonly AnyMouseButtonObservableBase source;
            readonly Mouse mouse;
            readonly MouseButton[] buffer;
            int bufferCount;

            public FrameRunnerWorkItem(AnyMouseButtonObservableBase source, Observer<MouseButton> observer, Mouse mouse, CancellationToken cancellationToken) : base(observer, cancellationToken)
            {
                this.source = source;
                this.mouse = mouse;
                this.buffer = new MouseButton[AllMouseButtons.Length];
            }

            protected override bool MoveNextCore(long _)
            {
                bufferCount = 0;

                var mouse = this.mouse ?? Mouse.current;
                if (mouse == null)
                {
                    return true;
                }

                for (int i = 0; i < AllMouseButtons.Length; i++)
                {
                    var button = AllMouseButtons[i];
                    if (source.CheckMouse(mouse, button))
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
