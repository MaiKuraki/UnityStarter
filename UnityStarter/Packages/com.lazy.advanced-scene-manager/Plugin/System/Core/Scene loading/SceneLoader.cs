using System.Collections;
using AdvancedSceneManager.Models;
using UnityEngine;

namespace AdvancedSceneManager.Core
{

    /// <summary>Specifies a scene loader.</summary>
    public abstract class SceneLoader
    {

        public struct Indicator
        {

            public string text { get; set; }
            public string tooltip { get; set; }
            public bool useFontAwesome { get; set; }
            public bool useFontAwesomeBrands { get; set; }
            public Color? color { get; set; }

        }

        /// <summary>Gets the key for the specified scene loader.</summary>
        public static string GetKey<T>() where T : SceneLoader => typeof(T).FullName;

        /// <summary>Gets the key for the specified scene loader.</summary>
        public static string GetKey<T>(T obj) where T : SceneLoader => obj.GetType().FullName;

        /// <summary>Gets the key for this scene loader.</summary>
        /// <remarks>This is equal to <see cref="System.Type.FullName"/>.</remarks>
        public string Key => GetKey(this);

        /// <summary>Specifies the text to display on the toggle in scene popup. Only has an effect if <see cref="isGlobal"/> is <see langword="false"/>.</summary>
        public virtual string sceneToggleText { get; }

        /// <summary>Specifies the indicator on scene fields for this scene loader.</summary>
        public virtual Indicator indicator { get; }

        /// <summary>Specifies if this scene loader will can be applied to all scenes. Otherwise scenes will have to be explicitly flagged to open with this loader.</summary>
        /// <remarks>
        /// To flag a scene to be opened with this loader, the following two methods can be used:
        /// <para>If <see cref="sceneToggleText"/> is non-empty, a toggle will be displayed in scene popup.</para>
        /// <para>Programmatically <see cref="Scene.SetSceneLoader{T}"/> can be used.</para>
        /// </remarks>
        public virtual bool isGlobal { get; } = true;

        /// <summary>Gets whatever this scene loader can handle the scene.</summary>
        public virtual bool CanHandleScene(Scene scene) => true;

        /// <summary>Specifies whatever this loader will run outside of play mode or not.</summary>
        public virtual bool activeOutsideOfPlayMode { get; }

        /// <summary>Specifies whatever this loader will run in play mode or not.</summary>
        public virtual bool activeInPlayMode { get; } = true;

        /// <summary>Specifies whatever scenes using this loader should be added to build settings scene list.</summary>
        public virtual bool addScenesToBuildSettings { get; } = true;

        /// <summary>Gets whatever this loader may be activated in the current context.</summary>
        public bool canBeActivated =>
            Application.isPlaying
            ? activeInPlayMode
            : activeOutsideOfPlayMode;

        /// <summary>Loads the scene specified in e.scene.</summary>
        public abstract IEnumerator LoadScene(Scene scene, SceneLoadArgs e);

        /// <summary>Unloads the scene specified in e.scene.</summary>
        public abstract IEnumerator UnloadScene(Scene scene, SceneUnloadArgs e);

    }

}
