using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AdvancedSceneManager.Utility
{

    /// <summary>An utility class that helps with running coroutines detached from <see cref="MonoBehaviour"/>.</summary>
    public static partial class CoroutineUtility
    {

        internal static CoroutineRunner m_runner;

        static CoroutineRunner Runner()
        {

            if (m_runner)
                return m_runner;

            if (UnityCompatibiltyHelper.FindFirstObjectByType<CoroutineRunner>() is CoroutineRunner runner && runner)
            {
                m_runner = runner;
                return m_runner;
            }

            var obj = new GameObject("Coroutine runner");
            Object.DontDestroyOnLoad(obj);
            m_runner = obj.AddComponent<CoroutineRunner>();
            return m_runner;

        }

        #region Run

        /// <summary>Runs the action after the specified time.</summary>
        public static void Run(Action action, TimeSpan after, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerName = "") =>
            Run(action, after: (float)after.TotalSeconds, false, null, callerFile, callerLine, callerName);

        /// <summary>Runs the action after the specified time.</summary>
        public static void Run(Action action, float? after = null, bool nextFrame = false, Func<bool> when = null, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerName = "")
        {
            var desc = new StringBuilder("Run: ").Append(callerName).Append("()");

            if (after.HasValue)
                desc.Append(", ").Append(after.Value).Append("s");
            else if (nextFrame)
                desc.Append(", next frame");
            else if (when != null)
                desc.Append(", when condition is true");

            _ = Coroutine()?.StartCoroutine(null, desc.ToString(), callerFile, callerLine);
            IEnumerator Coroutine()
            {
                if (after.HasValue)
                    yield return new WaitForSeconds(after.Value);
                else if (nextFrame)
                    yield return null;
                else if (when != null && !when.Invoke())
                    yield return null;

                action?.Invoke();

            }
        }

        #endregion
        #region StartCoroutine

        /// <summary>Runs the coroutine using <see cref="CoroutineUtility"/>, which means it won't be tied to a <see cref="MonoBehaviour"/> and will persist through scene close.</summary>
        /// <remarks>You may yield return this method.</remarks>
        public static GlobalCoroutine StartCoroutineGlobal(this MonoBehaviour _, IEnumerator coroutine, Action onComplete = null, string description = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
            StartCoroutine(coroutine, onComplete, description, callerFile, callerLine);

        /// <inheritdoc cref="StartCoroutineGlobal(MonoBehaviour, IEnumerator, Action, string, string, int)"/>
        public static GlobalCoroutine StartCoroutine(this IEnumerator coroutine, Action onComplete = null, string description = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {

            if (coroutine == null)
                return null;

            var c = GlobalCoroutine.Get(onComplete, (GetCaller(), callerFile.Replace("\\", "/"), callerLine), description);

            if (Application.isPlaying)
                Runner().Add(coroutine, c);
            else
            {
#if COROUTINES && UNITY_EDITOR
                //If com.unity.editorcoroutines is installed, then we'll use that to provide editor functionality
                Unity.EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutineOwnerless(CoroutineRunner.RunCoroutine(coroutine, c));
#endif
            }

            return c;

        }

        #endregion
        #region Timer

        /// <summary>Runs the action every interval.</summary>
        /// <remarks>Automatically stops when <see cref="Application.isPlaying"/> changes.</remarks>
        public static GlobalCoroutine Timer(Action action, TimeSpan interval, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerName = "")
        {
            return Coroutine()?.StartCoroutine(null, "Timer", callerFile, callerLine);
            IEnumerator Coroutine()
            {
                var state = Application.isPlaying;
                while (state == Application.isPlaying)
                {
                    action.Invoke();
                    yield return new WaitForSeconds((float)interval.TotalSeconds);
                }
            }
        }
        /// <summary>Runs the action every interval. Using unscaled time.</summary>
        /// <remarks>Automatically stops when <see cref="Application.isPlaying"/> changes.</remarks>
        public static GlobalCoroutine TimerRealtime(Action action, TimeSpan interval, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerName = "")
        {
            return Coroutine()?.StartCoroutine(null, "Timer", callerFile, callerLine);
            IEnumerator Coroutine()
            {
                var state = Application.isPlaying;
                while (state == Application.isPlaying)
                {
                    action.Invoke();
                    yield return new WaitForSecondsRealtime((float)interval.TotalSeconds);
                }
            }
        }

        #endregion
        #region Chain

        /// <summary>Runs the coroutines in sequence, wrapped in a single <see cref="GlobalCoroutine"/>.</summary>
        public static GlobalCoroutine Chain(params Func<IEnumerator>[] coroutines)
        {

            return Coroutine().StartCoroutine();

            IEnumerator Coroutine()
            {
                foreach (var coroutine in coroutines)
                    yield return coroutine?.Invoke();
            }

        }

        /// <summary>Runs the coroutines in sequence, wrapped in a single <see cref="GlobalCoroutine"/>.</summary>
        public static GlobalCoroutine Chain(IEnumerable<IEnumerator> coroutines)
        {

            return Coroutine().StartCoroutine();

            IEnumerator Coroutine()
            {
                foreach (var coroutine in coroutines)
                    yield return coroutine;
            }

        }

        #endregion
        #region Stop

        /// <summary>Stops the coroutine.</summary>
        public static void StopCoroutine(GlobalCoroutine coroutine) =>
            coroutine?.Stop();

        /// <summary>Stops all global coroutines.</summary>
        /// <remarks>No effect if outside of play mode.</remarks>
        public static void StopAllCoroutines()
        {
            if (Application.isPlaying && m_runner)
                Runner().Clear();
        }

        #endregion
        #region WaitAll

        /// <summary>Wait for all coroutines to complete.</summary>
        public static IEnumerator WaitAll(params IEnumerator[] coroutines) =>
            coroutines?.WaitAll();

        /// <summary>Wait for all coroutines to complete.</summary>
        public static IEnumerator WaitAll(this IEnumerable<IEnumerator> coroutines, Func<bool> isCancelled = null, string debugText = null)
        {
            var coroutine = coroutines.Select(c => c.StartCoroutine(description: debugText)).ToArray();
            while (coroutine.Any(c => !c.isComplete))
            {
                if (isCancelled?.Invoke() ?? false)
                {
                    foreach (var c in coroutine)
                        c.Stop();
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>Wait for all coroutines to complete.</summary>
        public static IEnumerator WaitAll(params GlobalCoroutine[] coroutines) =>
            coroutines?.WaitAll();

        /// <summary>Wait for all coroutines to complete.</summary>
        public static IEnumerator WaitAll(this GlobalCoroutine[] coroutines, Func<bool> isCancelled = null)
        {
            while (coroutines.Any(c => !c.isComplete))
            {

                if (isCancelled?.Invoke() ?? false)
                {
                    foreach (var c in coroutines)
                        c.Stop();
                    yield break;
                }

                yield return null;

            }
        }

        #endregion

        /// <summary>Get caller of the current method.</summary>
        static MethodBase GetCaller()
        {

#if UNITY_WEBGL
            return null;
#else
            var stackTrace = new System.Diagnostics.StackTrace();
            var stackFrames = stackTrace.GetFrames();
            var callingFrame = stackFrames.ElementAtOrDefault(2);

            return callingFrame?.GetMethod();
#endif

        }

    }

}
