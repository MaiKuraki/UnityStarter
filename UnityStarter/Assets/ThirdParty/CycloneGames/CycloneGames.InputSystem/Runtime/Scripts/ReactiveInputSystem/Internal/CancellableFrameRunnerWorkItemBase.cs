using System;
using System.Threading;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    internal static class CancellationTokenExtensions
    {
        public static CancellationTokenRegistration UnsafeRegister(this CancellationToken cancellationToken, Action<object> callback, object state)
        {
            return cancellationToken.Register(callback, state, useSynchronizationContext: false);
        }
    }

    // source: https://github.com/Cysharp/R3/blob/main/src/R3/Internal/CancellableFrameRunnerWorkItemBase.cs

    internal abstract class CancellableFrameRunnerWorkItemBase<T> : IFrameRunnerWorkItem, IDisposable
    {
        readonly Observer<T> observer;
        CancellationTokenRegistration cancellationTokenRegistration;
        int isDisposed;

        public CancellableFrameRunnerWorkItemBase(Observer<T> observer, CancellationToken cancellationToken)
        {
            this.observer = observer;

            if (cancellationToken.CanBeCanceled)
            {
                this.cancellationTokenRegistration = cancellationToken.UnsafeRegister(static state =>
                {
                    var s = (CancellableFrameRunnerWorkItemBase<T>)state!;
                    s.observer.OnCompleted();
                    s.Dispose();
                }, this);
            }
        }

        public bool MoveNext(long frameCount)
        {
            if (Volatile.Read(ref isDisposed) != 0)
            {
                return false;
            }

            if (observer.IsDisposed)
            {
                Dispose();
                return false;
            }

            return MoveNextCore(frameCount);
        }

        protected abstract bool MoveNextCore(long frameCount);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) == 0)
            {
                cancellationTokenRegistration.Dispose();
                DisposeCore();
            }
        }

        protected virtual void DisposeCore() { }

        protected void PublishOnNext(T value)
        {
            observer.OnNext(value);
        }

        protected void PublishOnErrorResume(Exception error)
        {
            observer.OnErrorResume(error);
        }

        protected void PublishOnCompleted(Exception error)
        {
            observer.OnCompleted(error);
            Dispose();
        }

        protected void PublishOnCompleted()
        {
            observer.OnCompleted();
            Dispose();
        }
    }
}
