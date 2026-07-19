using System;
using R3;
using R3.Collections;

namespace CycloneGames.InputSystem.Runtime
{
    public sealed class InputSystemFrameProvider : FrameProvider
    {
        public static readonly FrameProvider BeforeUpdate = new InputSystemFrameProvider();
        public static readonly FrameProvider AfterUpdate = new InputSystemFrameProvider();

        FreeListCore<IFrameRunnerWorkItem> list;
        long frameCount;
        readonly object gate = new();

        InputSystemFrameProvider()
        {
            list = new FreeListCore<IFrameRunnerWorkItem>(gate);
        }

        internal void OnUpdate()
        {
            var span = list.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (item != null)
                {
                    try
                    {
                        if (!item.MoveNext(frameCount))
                        {
                            list.Remove(i);
                        }
                    }
                    catch (Exception ex) when (
                        ex is not OutOfMemoryException &&
                        ex is not AccessViolationException &&
                        ex is not StackOverflowException)
                    {
                        list.Remove(i);
                        try
                        {
                            ObservableSystem.GetUnhandledExceptionHandler().Invoke(ex);
                        }
                        catch (Exception handlerException) when (
                            handlerException is not OutOfMemoryException &&
                            handlerException is not AccessViolationException &&
                            handlerException is not StackOverflowException)
                        {
                            // The frame loop must stay alive even if the global exception handler fails.
                        }
                    }
                }
            }

            frameCount++;
        }

        internal void Reset()
        {
            var span = list.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (!(span[i] is IDisposable disposable)) continue;
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception) when (
                    exception is not OutOfMemoryException &&
                    exception is not AccessViolationException &&
                    exception is not StackOverflowException)
                {
                    try
                    {
                        ObservableSystem.GetUnhandledExceptionHandler().Invoke(exception);
                    }
                    catch (Exception handlerException) when (
                        handlerException is not OutOfMemoryException &&
                        handlerException is not AccessViolationException &&
                        handlerException is not StackOverflowException)
                    {
                    }
                }
            }

            list.Clear(removeArray: true);
            frameCount = 0;
        }

        public override long GetFrameCount()
        {
            return frameCount;
        }

        public override void Register(IFrameRunnerWorkItem callback)
        {
            list.Add(callback, out _);
        }
    }
}
