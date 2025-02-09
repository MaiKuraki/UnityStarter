using System;
using System.Collections;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Defaults
{

    /// <summary>A default loading screen script. Displays progress with a bouncing icon.</summary>
    public class IconBounceLoadingScreen : FadeLoadingScreen
    {

        public Vector2 IconStartSize = new Vector2(5000, 5000);
        public float IconStartRotationZ = -50f;
        public float duration = 1.4f;

        public RectTransform IconTransform;
        public RectTransform BackgroundTransform;

        public override IEnumerator OnOpen()
        {
            yield return FadeIn();
            yield return LerpFloat(AnimateTick, 0, 1, duration).StartCoroutine();
        }

        public override IEnumerator OnClose()
        {
            yield return LerpFloat(AnimateTick, 1, 0, duration).StartCoroutine();
            yield return FadeOut();
        }

        void AnimateTick(float t)
        {

            if (!IconTransform || !BackgroundTransform)
                return;

            var iconSize = Vector2.Lerp(IconStartSize, new Vector2(0, 0), t);
            IconTransform.sizeDelta = iconSize;

            var rotation = Mathf.Lerp(IconStartRotationZ, 0, t);
            IconTransform.rotation = Quaternion.Euler(0, 0, rotation);

            BackgroundTransform.rotation = Quaternion.Euler(0, 0, 0);

        }

        IEnumerator LerpFloat(Action<float> @return, float from, float to, float duration = 2, Action<bool> Callback = null)
        {

            var i = 0f;
            var rate = 1f / duration;

            while (i < 1f)
            {
                i += Time.deltaTime * rate;
                @return(Mathf.Lerp(from, to, Mathf.SmoothStep(0.0f, 1.0f, i)));
                yield return null;
            }

            @return(to);
            Callback?.Invoke(true);

        }

    }

}
