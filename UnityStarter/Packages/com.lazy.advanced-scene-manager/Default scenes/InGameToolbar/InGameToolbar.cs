using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Callbacks;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Defaults
{

    [AddComponentMenu("")]
    class InGameToolbar : MonoBehaviour, ISceneOpenCoroutine, ISceneCloseCoroutine
    {

        VisualElement rootElement;
        VisualElement panel;

        void OnEnable()
        {

            rootElement = GetComponent<UIDocument>().rootVisualElement;
            rootElement.style.opacity = 0;

            panel = rootElement.Q("panel");

            AddListeners();
            SetupButtons();
            SetupOperations();
            Refresh();
            this.ASMScene().keepOpenWhenCollectionsClose = true;
            this.EnsureCameraExists();

        }

        public IEnumerator OnSceneOpen()
        {
            yield return LerpUtility.Lerp(0, 1, 0.25f, (t) => rootElement.style.opacity = t);
        }

        public IEnumerator OnSceneClose()
        {
            yield return LerpUtility.Lerp(1, 0, 0.25f, (t) => rootElement.style.opacity = t);
        }

        void OnDisable()
        {
            RemoveListeners();
        }

        void Update()
        {
            RefreshOperations();
        }

        #region Make sure we're interactable

        UnityEngine.UI.GraphicRaycaster[] raycasters = Array.Empty<UnityEngine.UI.GraphicRaycaster>();

        bool isHover;
        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout)
            {
                Vector2 mousePosition = Event.current.mousePosition;
                var isHoverNew = IsPointerOverElement(panel, mousePosition);

                if (isHoverNew != isHover)
                {

                    if (isHoverNew)
                    {
#if UNITY_2022_2_OR_NEWER
                        raycasters = FindObjectsByType<UnityEngine.UI.GraphicRaycaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
                        raycasters = FindObjectsOfType<UnityEngine.UI.GraphicRaycaster>().Where(r => r.enabled).ToArray();
#endif

                        foreach (var raycaster in raycasters)
                        {
                            raycaster.enabled = false;
                        }
                    }
                    else
                    {

                        foreach (var raycaster in raycasters)
                        {
                            if (raycaster)
                                raycaster.enabled = true;
                        }

                        raycasters = Array.Empty<UnityEngine.UI.GraphicRaycaster>();

                    }

                }

                isHover = isHoverNew;

            }
        }

        private bool IsPointerOverElement(VisualElement element, Vector2 mousePosition)
        {
            if (element == null || element.panel == null)
                return false;

            // Convert mouse position to local coordinates of the panel
            var root = element.panel.visualTree;
            var panelPosition = root.WorldToLocal(mousePosition);

            // Check if the local mouse position is within the bounds of the element
            return element.worldBound.Contains(panelPosition);
        }

        #endregion
        #region Listeners

        void AddListeners()
        {
            RemoveListeners();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneManager_sceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += SceneManager_sceneUnloaded;
        }

        void RemoveListeners()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= SceneManager_sceneUnloaded;
        }

        void Runtime_sceneOpened(Scene obj) => Refresh();
        void SceneManager_sceneUnloaded(UnityEngine.SceneManagement.Scene arg0) => Refresh();
        void SceneManager_sceneLoaded(UnityEngine.SceneManagement.Scene arg0, UnityEngine.SceneManagement.LoadSceneMode arg1) => Refresh();

        #endregion
        #region Buttons

        Button reopenCollectionButton;

        void SetupButtons()
        {

            rootElement.Q<Button>("button-restart").clicked += Restart;
            rootElement.Q<Button>("button-quit").clicked += Quit;

            reopenCollectionButton = rootElement.Q<Button>("button-reopen-collection");
            reopenCollectionButton.clicked += ReopenCollection;
        }

        void Restart() => SceneManager.app.Restart();

        bool busy = false;
        void ReopenCollection()
        {
            if (busy) return;
            busy = true;

            Coroutine().StartCoroutine(() => busy = false);

            static IEnumerator Coroutine()
            {
                var openCollection = SceneManager.openCollection;
                yield return openCollection.Close().Open(openCollection).With(SceneManager.profile.loadingScene);
                SceneManager.runtime.Track(openCollection);
            }
        }

        void Quit() => SceneManager.app.Quit();

        #endregion
        #region Operations

        Label queueText;
        Label runningText;

        void SetupOperations()
        {

            queueText = rootElement.Q<Label>("text-queued");
            runningText = rootElement.Q<Label>("text-running");
            RefreshOperations();

        }

        void RefreshOperations()
        {
            queueText.text = SceneManager.runtime.queuedOperations.Count().ToString();
            runningText.text = SceneManager.runtime.runningOperations.Count().ToString();
        }

        #endregion
        #region Lists

        void Refresh()
        {

            var scenes = SceneManager.runtime.openScenes.ToList();
            var splashScreens = Take(scenes, s => s.isSplashScreen);
            var loadingScreens = Take(scenes, s => s.isLoadingScreen);
#if ENABLE_INPUT_SYSTEM && INPUTSYSTEM
            var bindingScenes = Take(scenes, SceneBindingUtility.WasOpenedByBinding);
#endif
            var persistent = Take(scenes, s => s.isPersistent && !(s.openedBy && s.openedBy.isOpen));
            var collectionsScenes = Take(scenes, s => s.openedBy);
            var standalone = scenes.ToArray();
            var untracked =
                SceneUtility.GetAllOpenUnityScenes().
                Where(s => !FallbackSceneUtility.IsFallbackScene(s) &&
                           (!s.ASMScene() || !s.ASMScene().isOpen));

            AddToList(splashScreens.Select(s => s.name), rootElement.Q<Foldout>("group-splash-scenes"));
            AddToList(loadingScreens.Select(s => s.name), rootElement.Q<Foldout>("group-loading-scenes"));
            AddToList(persistent.Select(s => s.name), rootElement.Q<Foldout>("group-persistent"));
#if ENABLE_INPUT_SYSTEM && INPUTSYSTEM
            AddToList(bindingScenes.Select(s => s.name), rootElement.Q<Foldout>("group-binding-scenes"));
#endif
            AddToList(standalone.Select(s => s.name), rootElement.Q<Foldout>("group-standalone"));
            AddToList(untracked.Select(s => s.name), rootElement.Q<Foldout>("group-untracked"));

            var collectionList = rootElement.Q<Foldout>("group-collections");
            collectionList.Clear();
            var collections = collectionsScenes.GroupBy(s => s.openedBy).ToArray();
            foreach (var collection in collections)
                AddToList(
                    name: collection.Key.title + (collection.Key.isOpenAdditive ? " <color=#FFFFFF11>(additive)" : ""),
                    names: collection.Select(s => s.name + (s.isPersistent ? " <color=#FFFFFF11>(persistent)" : "")),
                    element: collectionList);

            rootElement.Query().ForEach(e => e.style.cursor = new(StyleKeyword.Initial));

        }

        Scene[] Take(List<Scene> scenes, Predicate<Scene> predicate)
        {
            var taken = scenes.Where(s => predicate(s)).ToArray();
            scenes.RemoveAll(predicate);
            return taken;
        }

        void AddToList(string name, IEnumerable<string> names, Foldout element)
        {
            var foldout = new Foldout { text = name };
            foldout.style.marginRight = 22;
            AddToList(names, foldout);
            element.Add(foldout);
            element.style.display = names.Count() > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void AddToList(IEnumerable<string> names, Foldout element)
        {

            element.Clear();
            foreach (var name in names)
            {
                var label = new Label(name);
                label.style.marginLeft = 22;
                label.style.marginRight = 22;
                element.Add(label);
            }

            element.style.display = names.Count() > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        }

        #endregion

    }

}

