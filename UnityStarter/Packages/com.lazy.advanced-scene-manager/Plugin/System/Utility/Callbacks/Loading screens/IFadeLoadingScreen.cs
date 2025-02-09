using UnityEngine;

namespace AdvancedSceneManager.Loading
{

    /// <summary>Used to pass arguments from <see cref="LoadingScreenUtility.FadeIn(LoadingScreen, float, Color?)"/></summary>
    public interface IFadeLoadingScreen
    {
        /// <summary>Specifies the fade duration.</summary>
        float fadeDuration { get; set; }
        /// <summary>Specifies the color of the fade.</summary>
        Color color { get; set; }
    }

}