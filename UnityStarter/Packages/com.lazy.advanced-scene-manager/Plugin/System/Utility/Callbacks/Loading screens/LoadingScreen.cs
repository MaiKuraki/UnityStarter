using System.Collections;
using AdvancedSceneManager.Core;
using UnityEngine;

namespace AdvancedSceneManager.Loading
{

    /// <summary>A class that contains callbacks for loading screens.</summary>
    /// <remarks><see cref="SplashScreen"/> and <see cref="LoadingScreen"/> cannot co-exist within the same scene.</remarks>
    public abstract class LoadingScreen : LoadingScreenBase
    {

        /// <summary>The current scene operation that this loading screen is associated with. May be null for the first few frames, before loading has actually begun.</summary>
        public SceneOperation operation { get; internal set; }

        /// <summary>Called when loading scene is opened.</summary>
        /// <remarks>Use this callback to show your loading screen, the scene manager will wait until its done.</remarks>
        public abstract override IEnumerator OnOpen();

        /// <summary>Called when loading scene is closed.</summary>
        /// <remarks>Use this callback to hide your loading screen, the scene manager will wait until its done.</remarks>
        public abstract override IEnumerator OnClose();

        [SerializeField]
        private bool isLoadingScreen = true;

        public virtual void OnValidate()
        {
            if (!isLoadingScreen)
                isLoadingScreen = true;
        }

    }

}