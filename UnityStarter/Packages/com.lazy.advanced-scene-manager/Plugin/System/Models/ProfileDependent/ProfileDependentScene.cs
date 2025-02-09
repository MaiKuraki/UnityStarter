using System;
using AdvancedSceneManager.Core;
using UnityEngine;

namespace AdvancedSceneManager.Models.Utility
{

    /// <summary>Represents a <see cref="Scene"/> that changes depending on active <see cref="Profile"/>.</summary>
    [CreateAssetMenu(menuName = "Advanced Scene Manager/Profile dependent scene")]
    public class ProfileDependentScene : ProfileDependent<Scene>, Scene.IMethods, Scene.IMethods.IEvent
    {

        public static implicit operator Scene(ProfileDependentScene instance) =>
            instance.GetModel(out var scene) ? scene : null;

        #region IMethods

        public SceneOperation Open() => SceneManager.runtime.Open(this);
        public SceneOperation OpenAndActivate() => SceneManager.runtime.OpenAndActivate(this);
        public SceneOperation ToggleOpen() => SceneManager.runtime.ToggleOpen(this);
        public SceneOperation Close() => SceneManager.runtime.Close(this);
        public SceneOperation Preload(Action onPreloaded = null) => SceneManager.runtime.Preload(this, onPreloaded);
        public SceneOperation FinishPreload() => SceneManager.runtime.FinishPreload();
        public SceneOperation CancelPreload() => SceneManager.runtime.CancelPreload();
        public SceneOperation OpenWithLoadingScreen(Scene loadingScreen) => SceneManager.runtime.OpenWithLoadingScreen(this, loadingScreen);
        public SceneOperation CloseWithLoadingScreen(Scene loadingScreen) => SceneManager.runtime.CloseWithLoadingScreen(this, loadingScreen);
        public void SetActive() => SceneManager.runtime.Activate(this);

        [Obsolete("DiscardPreload is obsolete, please use CancelPreload instead.")]
        public SceneOperation DiscardPreload() => SceneManager.runtime.DiscardPreload();

        #endregion
        #region IEvent

        public void _Open() => Open();
        public void _OpenAndActivate() => OpenAndActivate();
        public void _ToggleOpenState() => ToggleOpen();
        public void _ToggleOpen() => ToggleOpen();
        public void _Close() => Close();
        public void _Preload() => Preload();
        public void _FinishPreload() => FinishPreload();
        public void _CancelPreload() => CancelPreload();
        public void _OpenWithLoadingScreen(Scene loadingScene) => OpenWithLoadingScreen(loadingScene);
        public void _CloseWithLoadingScreen(Scene loadingScene) => CloseWithLoadingScreen(loadingScene);
        public void _SetActive() => SetActive();

        [Obsolete("DiscardPreload is obsolete, please use CancelPreload instead.")]
        public void _DiscardPreload() => DiscardPreload();
        #endregion

    }

}
