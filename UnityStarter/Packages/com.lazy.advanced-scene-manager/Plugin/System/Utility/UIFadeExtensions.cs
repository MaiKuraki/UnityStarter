using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Provides extension methods for <see cref="CanvasGroup"/>.</summary>
    public static class UIFadeExtensions
    {

        /// <summary>Animates the alpha of a <see cref="CanvasGroup"/>.</summary>
        public static IEnumerator Fade(this CanvasGroup group, float to, float duration, bool setBlocksRaycasts = true)
        {

            if (!group || !group.gameObject.activeInHierarchy)
                yield break;

            if (setBlocksRaycasts)
                group.blocksRaycasts = true;

            if (group.alpha == to)
                yield break;

            yield return LerpUtility.Lerp(group.alpha, to, duration, t =>
            {

                if (!group)
                    return;

                group.alpha = t;

                if (setBlocksRaycasts)
                    group.blocksRaycasts = group.alpha > 0;

            });

        }

        /// <summary>Animates the alpha of a <see cref="Graphic"/>.</summary>
        public static IEnumerator Fade(this Graphic text, float to, float duration, bool ignoreTimeScale)
        {
            text.CrossFadeAlpha(to, duration, ignoreTimeScale);
            yield return new WaitForSeconds(duration);
        }

        /// <summary>Animates the alpha of all <see cref="Graphic"/> found on <paramref name="element"/> and children.</summary>
        public static IEnumerator Fade(this RectTransform element, float to, float duration, bool ignoreTimeScale)
        {
            var graphics = element.GetComponentsInChildren<Graphic>();
            foreach (var g in graphics)
                g.CrossFadeAlpha(to, duration, ignoreTimeScale);

            yield return new WaitForSeconds(duration);
        }

        /// <summary>Animates the alpha of all <see cref="Graphic"/> found on <paramref name="element"/> and children.</summary>
        public static IEnumerator Fade(this UIBehaviour element, float to, float duration, bool ignoreTimeScale)
        {
            var graphics = element.GetComponentsInChildren<Graphic>();
            foreach (var g in graphics)
                g.CrossFadeAlpha(to, duration, ignoreTimeScale);

            yield return new WaitForSeconds(duration);
        }

    }

}