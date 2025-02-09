using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Models.Helpers
{

    /// <summary>Provides access to the default ASM scenes.</summary>
    public class DefaultScenes : ScriptableObject
    {

        /// <summary>Gets the default ASM splash scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene splashASMScene => Find("Splash ASM");

        /// <summary>Gets the default fade splash scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene splashFadeScene => Find("Splash Fade");

        /// <summary>Gets the default fade loading scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene fadeScene => Find("Fade");

        /// <summary>Gets the default progress bar loading scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene progressBarScene => Find("ProgressBar");

        /// <summary>Gets the default progress bar loading scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene totalProgressBarScene => Find("TotalProgressBar");

        /// <summary>Gets the default icon bounce loading scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene iconBounceScene => Find("IconBounce");

        /// <summary>Gets the default press any button loading scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene pressAnyKeyScene => Find("PressAnyKey");

        /// <summary>Gets the default quote loading scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene quoteScene => Find("Quote");

        /// <summary>Gets the default pause scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene pauseScene => Find("Pause");

        /// <summary>Gets the default in-game-toolbar scene.</summary>
        /// <remarks>May be <see langword="null"/> if scene has been removed, or is not imported.</remarks>
        public Scene inGameToolbarScene => Find("InGameToolbar");

        Scene Find(string name) =>
            Enumerate().Find(name);

        /// <summary>Enumerates all imported default scenes.</summary>
        public IEnumerable<Scene> Enumerate() =>
            SceneManager.assets.scenes.NonNull().Where(s => s.isDefaultASMScene);

    }

}
