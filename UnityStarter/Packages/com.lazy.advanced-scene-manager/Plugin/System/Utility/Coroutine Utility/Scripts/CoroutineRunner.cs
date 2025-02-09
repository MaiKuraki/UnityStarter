using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Reflection;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AdvancedSceneManager.Utility
{

    [ExecuteAlways]
    [AddComponentMenu("")]
    internal partial class CoroutineRunner : MonoBehaviour
    {

#if UNITY_EDITOR

        void Start()
        {

            EditorApplication.playModeStateChanged += (mode) =>
            {
                if (mode == PlayModeStateChange.ExitingPlayMode)
                    if (this && gameObject)
                        Destroy(gameObject);
            };

        }

#endif

        void OnDestroy()
        {
            foreach (var coroutine in m_coroutines.Keys.ToArray())
                coroutine?.Stop();
        }

        /// <summary>Occurs when a coroutine is added or removed.</summary>
        public static event Action OnListChanged;

        readonly Dictionary<GlobalCoroutine, Coroutine> m_coroutines = new();
        public IReadOnlyCollection<GlobalCoroutine> coroutines => m_coroutines.Keys;

        public void Add(IEnumerator enumerator, GlobalCoroutine coroutine)
        {
            if (!this || !gameObject)
                return;

            m_coroutines.Add(coroutine, null);
            m_coroutines[coroutine] = StartCoroutine(RunCoroutine(
                enumerator,
                coroutine,
                onDone: () =>
                {
                    _ = m_coroutines.Remove(coroutine);
                    OnListChanged?.Invoke();
                }));
            OnListChanged?.Invoke();
        }

        public void Clear()
        {
            foreach (var coroutine in coroutines)
                coroutine.Stop(isCancel: true);
            m_coroutines.Clear();
            OnListChanged?.Invoke();
        }

        internal void Stop(GlobalCoroutine coroutine)
        {
            if (m_coroutines.TryGetValue(coroutine, out var c))
            {
                StopCoroutine(c);
                _ = m_coroutines.Remove(coroutine);
                OnListChanged?.Invoke();
            }
        }

        public static IEnumerator RunCoroutine(IEnumerator c, GlobalCoroutine coroutine, Action onDone = null)
        {

            coroutine.OnStart();

            object rootUserData = null;
            if (CoroutineUtility.Events.enableEvents)
            {
                CoroutineUtility.Events.onCoroutineStarted?.Invoke(coroutine);
                rootUserData = CoroutineUtility.Events.onSubroutineStart?.Invoke(coroutine, null, level: 0, null, isPause: false);
            }

            yield return RunSub(c, 0, rootUserData);

            onDone?.Invoke();

            if (CoroutineUtility.Events.enableEvents)
            {
                CoroutineUtility.Events.onSubroutineEnd(coroutine, rootUserData);
                CoroutineUtility.Events.onCoroutineEnded(coroutine);
            }

            coroutine.Stop(isCancel: false);

            IEnumerator RunSub(IEnumerator sub, int level, object parentUserData)
            {

                while (sub.MoveNext())
                {

                    if (coroutine.isComplete)
                        yield break;

                    if (coroutine.isPaused)
                    {

                        var pauseUserData = CoroutineUtility.Events.enableEvents
                            ? CoroutineUtility.Events.onSubroutineStart?.Invoke(coroutine, null, level, parentUserData, isPause: true)
                            : null;

                        while (coroutine.isPaused)
                            yield return null;

                        if (CoroutineUtility.Events.enableEvents)
                            CoroutineUtility.Events.onSubroutineEnd?.Invoke(coroutine, pauseUserData);

                    }

                    var userData = CoroutineUtility.Events.enableEvents
                        ? CoroutineUtility.Events.onSubroutineStart?.Invoke(coroutine, sub.Current, level + 1, parentUserData, isPause: false)
                        : null;

                    if (sub.Current is IEnumerator subroutine)
                        yield return RunSub(subroutine, level + 1, userData);
                    else
                        yield return ConvertRuntimeYieldInstructionsToEditor(sub.Current);

                    if (CoroutineUtility.Events.enableEvents)
                        CoroutineUtility.Events.onSubroutineEnd?.Invoke(coroutine, userData);

                }

            }

        }

        static Type EditorWaitForSecondsType { get; } =
            Type.GetType($"Unity.EditorCoroutines.Editor.EditorWaitForSeconds, Unity.EditorCoroutines.Editor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", throwOnError: false);

        static object ConvertRuntimeYieldInstructionsToEditor(object obj)
        {

#if UNITY_EDITOR

            if (Application.isPlaying || EditorWaitForSecondsType == null)
                return obj;

            if (obj is WaitForSeconds waitForSeconds && typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance)?.GetValue(waitForSeconds) is float time)
                return Activator.CreateInstance(EditorWaitForSecondsType, new object[] { time });
            else if (obj is WaitForSecondsRealtime waitForSecondsRealtime)
                return Activator.CreateInstance(EditorWaitForSecondsType, new object[] { waitForSecondsRealtime.waitTime });

#endif

            return obj;

        }

    }

}
