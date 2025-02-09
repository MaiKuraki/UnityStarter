using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AdvancedSceneManager.Utility
{

    internal static class GlobalCoroutinePool
    {

        static readonly object @lock = new object();

        static readonly List<GlobalCoroutine> pool = new List<GlobalCoroutine>();

        /// <summary>Gets a recycled instance of <see cref="GlobalCoroutine"/>, if none exist, a new instance will be created.</summary>
        internal static GlobalCoroutine Get(Action onComplete, (MethodBase method, string file, int line) caller, string description)
        {
            Get(out var coroutine);
            coroutine.Construct(onComplete, caller, description);
            return coroutine;
        }

        //Called by GlobalCoroutine finalizer
        internal static void ReturnToPool(GlobalCoroutine coroutine)
        {
            lock (@lock)
                pool.Add(coroutine);
        }

        //Retreive or create instance
        static void Get(out GlobalCoroutine coroutine)
        {

            lock (@lock)
            {

                coroutine = null;

                if (pool.Contains(null))
                    _ = pool.RemoveAll(obj => obj == null);

                if (pool.Any())
                {
                    coroutine = pool[0];
                    pool.RemoveAt(0);
                }

                coroutine ??= new GlobalCoroutine();

            }

        }

    }

}
