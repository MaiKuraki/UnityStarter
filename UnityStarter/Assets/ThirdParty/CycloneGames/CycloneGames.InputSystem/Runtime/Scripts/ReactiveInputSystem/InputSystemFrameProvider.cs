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
                    catch (Exception ex)
                    {
                        list.Remove(i);
                        try
                        {
                            ObservableSystem.GetUnhandledExceptionHandler().Invoke(ex);
                        }
                        catch
                        {
                            // The frame loop must stay alive even if the global exception handler fails.
                        }
                    }
                }
            }

            frameCount++;
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
