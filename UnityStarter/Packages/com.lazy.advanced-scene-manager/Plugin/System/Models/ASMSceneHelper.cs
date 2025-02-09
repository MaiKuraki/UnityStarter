using System;
using System.Linq;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Models
{

    /// <summary>Represents scene helper. Contains functions for opening / closing collections and scenes from <see cref="UnityEngine.Events.UnityEvent"/>.</summary>
    [AddComponentMenu("")]
    public class ASMSceneHelper : ScriptableObject,
        SceneCollection.IMethods_Target, SceneCollection.IMethods_Target.IEvent,
        Scene.IMethods_Target, Scene.IMethods_Target.IEvent
    {

        /// <inheritdoc cref="Object.name"/>
        public new string name => base.name; //Prevent renaming from UnityEvent

        /// <summary>Gets a reference to scene helper.</summary>
        public static ASMSceneHelper instance => Assets.sceneHelper;

        #region SceneCollection.IMethods

        public void Open(SceneCollection collection) => collection.Open();
        public void Reopen(SceneCollection collection) => collection.Reopen();
        public void OpenAdditive(SceneCollection collection) => collection.Open();
        public SceneOperation Open(SceneCollection collection, bool openAll = false) => collection.Open(openAll);
        public SceneOperation OpenAdditive(SceneCollection collection, bool openAll = false) => collection.OpenAdditive(openAll);

        public SceneOperation Preload(SceneCollection collection, bool openAll = false) => collection.Preload(openAll);
        public SceneOperation PreloadAdditive(SceneCollection collection, bool openAll = false) => collection.PreloadAdditive(openAll);

        public SceneOperation ToggleOpen(SceneCollection collection, bool openAll = false) => collection.ToggleOpen(openAll);

        public SceneOperation Close(SceneCollection collection) => collection.Close();

        #endregion
        #region SceneCollection.IEvent

        public void _Open(SceneCollection collection) => SpamCheck.EventMethods.Execute(() => collection.Open());
        public void _Reopen(SceneCollection collection) => SpamCheck.EventMethods.Execute(() => collection.Reopen());
        public void _OpenAdditive(SceneCollection collection) => SpamCheck.EventMethods.Execute(() => collection.OpenAdditive());

        public void _Preload(SceneCollection collection) => SpamCheck.EventMethods.Execute(() => Preload(collection));
        public void _PreloadAdditive(SceneCollection collection) => SpamCheck.EventMethods.Execute(() => PreloadAdditive(collection));

        public void _ToggleOpen(SceneCollection collection) => SpamCheck.EventMethods.Execute(() => collection.ToggleOpen());
        public void _Close(SceneCollection collection) => SpamCheck.EventMethods.Execute(() => collection.Close());

        #endregion
        #region Scene.IMethods

        public SceneOperation Open(Scene scene) =>
            scene.Open();

        public SceneOperation OpenAndActivate(Scene scene) =>
            scene.OpenAndActivate();

        public SceneOperation ToggleOpenState(Scene scene) =>
            scene.ToggleOpen();

        public SceneOperation ToggleOpen(Scene scene) =>
            scene.ToggleOpen();

        public SceneOperation Close(Scene scene) =>
            scene.Close();

        public SceneOperation Preload(Scene scene, Action onPreloaded = null) =>
            scene.Preload(onPreloaded);

        public SceneOperation OpenWithLoadingScreen(Scene scene, Scene loadingScene) =>
            scene.OpenWithLoadingScreen(loadingScene);

        public SceneOperation CloseWithLoadingScreen(Scene scene, Scene loadingScene) =>
            scene.CloseWithLoadingScreen(loadingScene);

        [Obsolete]
        public void SetActive(Scene scene) =>
            scene.Activate();

        public void Activate(Scene scene) =>
            scene.Activate();

        #endregion
        #region Scene.IEvent

        public void _Open(Scene scene) =>
            SpamCheck.EventMethods.Execute(() => Open(scene));

        public void _OpenAndActivate(Scene scene) =>
            SpamCheck.EventMethods.Execute(() => OpenAndActivate(scene));

        public void _ToggleOpen(Scene scene) =>
            SpamCheck.EventMethods.Execute(() => ToggleOpenState(scene));

        public void _Close(Scene scene) =>
            SpamCheck.EventMethods.Execute(() => Close(scene));

        public void _Preload(Scene scene) =>
            SpamCheck.EventMethods.Execute(() => Preload(scene));

        [Obsolete]
        public void _SetActive(Scene scene) =>
            SpamCheck.EventMethods.Execute(() => Activate(scene));

        public void _Activate(Scene scene) =>
            SpamCheck.EventMethods.Execute(() => Activate(scene));

        #endregion
        #region Custom

        /// <summary>Open all scenes that starts with the specified name.</summary>
        public void OpenWhereNameStartsWith(string name) =>
            SpamCheck.EventMethods.Execute(() => SceneManager.runtime.Open(SceneManager.assets.scenes.Where(s => s.name.StartsWith(name) && s.isIncludedInBuilds).ToArray()));

        /// <inheritdoc cref="App.Quit"/>
        public void Quit() => SceneManager.app.Quit();

        /// <inheritdoc cref="App.Restart"/>
        public void Restart() => SpamCheck.EventMethods.Execute(() => SceneManager.app.Restart());

        /// <summary>Re-opens <see cref="Runtime.openCollection"/>.</summary>
        public void RestartCollection() => SpamCheck.EventMethods.Execute(() => SceneManager.openCollection.Open());

        public SceneOperation FinishPreload() => SceneManager.runtime.FinishPreload();
        public void _FinishPreload() => SpamCheck.EventMethods.Execute(() => FinishPreload());

        public SceneOperation CancelPreload() => SceneManager.runtime.CancelPreload();
        public void _CancelPreload() => SpamCheck.EventMethods.Execute(() => CancelPreload());

        [Obsolete("DiscardPreload is obsolete, please use CancelPreload instead.")]
        public void _DiscardPreload() => SpamCheck.EventMethods.Execute(() => CancelPreload());

        #endregion

    }

}
