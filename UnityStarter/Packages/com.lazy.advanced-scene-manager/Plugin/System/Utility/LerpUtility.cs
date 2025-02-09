using System;
using System.Collections;
using UnityEngine;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Provides some convinience functions for lerping.</summary>
    public static class LerpUtility
    {

        /// <summary>Lerp from <paramref name="start"/> to <paramref name="end"/> over <paramref name="duration"/> seconds.</summary>
        /// <param name="start">The start value.</param>
        /// <param name="end">The end value.</param>
        /// <param name="duration">The duration in seconds to lerp for.</param>
        /// <param name="callback">The callback each lerp interval.</param>
        /// <param name="onComplete">Callback when complete.</param>
        public static IEnumerator Lerp(float start, float end, float duration, Action<float> callback, Action onComplete = null)
        {

            var t = 0f;
            var time = 0f;

            while (t <= 1)
            {

                callback?.Invoke(Mathf.Lerp(start, end, t));

                time += Time.unscaledDeltaTime;
                t = time / duration;
                yield return null;

            }

            callback?.Invoke(end);
            onComplete?.Invoke();

        }

        /// <inheritdoc cref="Lerp(float, float, float, Action{float}, Action)"/>
        public static IEnumerator Lerp(Vector3 start, Vector3 end, float duration, Action<Vector3> callback, Action onComplete = null)
        {

            var t = 0f;
            var time = 0f;

            while (t <= 1)
            {

                callback?.Invoke(Vector3.Lerp(start, end, t));

                time += Time.unscaledDeltaTime;
                t = time / duration;
                yield return null;

            }

            callback?.Invoke(end);
            onComplete?.Invoke();

        }

        /// <inheritdoc cref="Lerp(float, float, float, Action{float}, Action)"/>
        public static IEnumerator Lerp(Vector2 start, Vector2 end, float duration, Action<Vector2> callback, Action onComplete = null)
        {

            var t = 0f;
            var time = 0f;

            while (t <= 1)
            {

                callback?.Invoke(Vector2.Lerp(start, end, t));

                time += Time.unscaledDeltaTime;
                t = time / duration;
                yield return null;

            }

            callback?.Invoke(end);
            onComplete?.Invoke();

        }

    }

}
