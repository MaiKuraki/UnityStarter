using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace AdvancedSceneManager.Utility
{

    /// <summary>
    /// <para>An utility for running actions on the main thread.</para>
    /// <para>Only usable in play mode.</para>
    /// </summary>
    public static class MainThreadUtility
    {

        /// <summary>Gets whatever <see cref="MainThreadUtility"/> is enabled, set to <see langword="false"/> in source code to disable.</summary>
        public static bool isEnabled => true;

        static GlobalCoroutine coroutine;

        static int mainThreadID;

        /// <summary>Gets if the thread we're currently on is the main thread.</summary>
        public static bool isOnMainThread =>
            Thread.CurrentThread.ManagedThreadId == mainThreadID;

        [RuntimeInitializeOnLoadMethod]
        static void OnLoad()
        {
            mainThreadID = Thread.CurrentThread.ManagedThreadId;
            Start();
        }

        #region Invoke

        /// <summary>Queues the function to be run on the main thread, during the next frame.</summary>
        /// <param name="func">The action to invoke.</param>
        public static T Invoke<T>(Func<T> func) =>
            Invoke(func, mainThread: true);

        /// <summary>Queues the action to be run on the main thread, during the next frame.</summary>
        /// <param name="action">The action to invoke.</param>
        public static void Invoke(Action action) =>
            Invoke(action, mainThread: true);

        /// <summary>Invokes the <paramref name="action"/>.</summary>
        /// <param name="func">The action to invoke.</param>
        /// <param name="mainThread">Queues the action to be run on the main thread, during the next frame.</param>
        public static T Invoke<T>(this Func<T> func, bool mainThread = false)
        {

            if (!Application.isPlaying)
                Debug.LogError("MainThreadUtility can only be used in play mode");

            if (func == null)
                return default;

            if (!mainThread)
                return func.Invoke();
            else
            {
                T value = default;
                Invoke(action: () => value = func.Invoke());
                return value;
            }

        }

        /// <summary>Invokes the <paramref name="action"/>.</summary>
        /// <param name="action">The action to invoke.</param>
        /// <param name="mainThread">Queues the action to be run on the main thread, during the next frame.</param>
        public static void Invoke(this Action action, bool mainThread = false)
        {

            if (action == null)
                return;

            if (!mainThread || isOnMainThread)
                action.Invoke();
            else
                lock (executeOnMainThread)
                    executeOnMainThread.Add(action);

        }

        #endregion
        #region Coroutine

        /// <summary>Starts main thread utility coroutine.</summary>
        public static void Start()
        {
            if (!IsRunning)
                coroutine = Coroutine().StartCoroutine(description: "Main Thread Dispatcher");
        }

        /// <summary>Stops main thread utility coroutine.</summary>
        public static void Stop() =>
            coroutine?.Stop();

        /// <summary>Gets if main thread utility is running.</summary>
        public static bool IsRunning =>
            coroutine?.isRunning ?? false;

        static IEnumerator Coroutine()
        {
            while (true)
            {
                Update();
                yield return null;
            }
        }

        #endregion

        //Invoke adds actions to here
        static readonly List<Action> executeOnMainThread = new List<Action>();

        //Update() copies actions to here, using lock, and then executes them
        static readonly List<Action> executeCopiedOnMainThread = new List<Action>();

        //Called from coroutine
        static void Update()
        {

            if (executeOnMainThread.Count == 0)
                return;

            executeCopiedOnMainThread.Clear();
            lock (executeOnMainThread)
            {
                executeCopiedOnMainThread.AddRange(executeOnMainThread);
                executeOnMainThread.Clear();
            }

            foreach (var action in executeCopiedOnMainThread.ToArray())
                action?.Invoke();

        }

    }

}
