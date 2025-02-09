using System.Collections;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedSceneManager.Defaults
{

    /// <summary>A default loading screen script. Fades screen out, then fades screen in when loading is done. Does not display progress.</summary>
    public class FadeLoadingScreen : LoadingScreen, IFadeLoadingScreen
    {

        public CanvasGroup fadeGroup;
        public Image fadeBackground;
        public float? fadeInDurationOverride;
        public float fadeDuration = 0.5f;
        public Color color;

        float IFadeLoadingScreen.fadeDuration
        {
            get => fadeDuration;
            set => fadeInDurationOverride = value;
        }

        Color IFadeLoadingScreen.color
        {
            get => color;
            set => color = value;
        }

        protected override void Start()
        {
            base.Start();
            if (fadeGroup)
                fadeGroup.alpha = 0;
        }

        public override IEnumerator OnOpen()
        {
            yield return FadeIn();
        }

        public override IEnumerator OnClose()
        {
            yield return FadeOut();
        }

        protected IEnumerator FadeIn()
        {

            fadeBackground.color = color; //Color can be changed when using FadeUtility methods

            if ((fadeInDurationOverride ?? fadeDuration) > 0)
                yield return fadeGroup.Fade(1, fadeInDurationOverride ?? fadeDuration);
            else
                fadeGroup.alpha = 1;

        }

        protected IEnumerator FadeOut()
        {
            yield return fadeGroup.Fade(0, fadeDuration);
        }

    }

}
