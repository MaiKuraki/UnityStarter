using System;
using System.Collections;
using UnityEngine;

namespace AdvancedSceneManager.Loading
{

    /// <summary>A class that contains callbacks for splash screens.</summary>
    /// <remarks><see cref="SplashScreen"/> and <see cref="LoadingScreen"/> cannot coexist within the same scene.</remarks>
    public abstract class SplashScreen : LoadingScreenBase
    {

        /// <summary>Called when splash scene is opened.</summary>
        /// <remarks>Use this callback to show your splash screen, the scene manager will wait until its done.</remarks>
        public abstract override IEnumerator OnOpen();

        /// <summary>Called when splash scene is about to close.</summary>
        /// <remarks>Use this callback to hide your splash screen, the scene manager will wait until its done.</remarks>
        public abstract override IEnumerator OnClose();

        [Obsolete("Splash scenes are now used the same way as startup loading scenes used to, please use OnOpen(), and OnClose() instead.")]
        public virtual IEnumerator DisplaySplashScreen()
        { yield break; }

        [SerializeField]
        private bool isSplashScreen = true;

        public virtual void OnValidate()
        {
            if (!isSplashScreen)
                isSplashScreen = true;
        }

    }

}
