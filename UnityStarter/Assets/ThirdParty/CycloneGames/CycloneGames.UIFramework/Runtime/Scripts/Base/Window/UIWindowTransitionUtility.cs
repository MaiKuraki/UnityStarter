using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    public static class UIWindowTransitionUtility
    {
        /// <summary>
        /// Returns the window's CanvasGroup, adding one when missing because transition
        /// animations require alpha control. A runtime-added group is a permanent component
        /// and is destroyed together with the window GameObject.
        /// </summary>
        public static CanvasGroup GetOrAddCanvasGroup(UIWindow window)
        {
            if (window == null)
            {
                return null;
            }

            CanvasGroup group = window.GetComponent<CanvasGroup>();
            return group != null ? group : window.gameObject.AddComponent<CanvasGroup>();
        }
    }
}
