using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UIElements;
using Scene = AdvancedSceneManager.Models.Scene;

namespace AdvancedSceneManager.Editor.Utility
{

    static class SceneOpenButtonsHelper
    {

        static SceneOpenButtonsHelper()
        {
            EditorSceneManager.sceneSaved += EditorSceneManager_sceneSaved;
            SceneImportUtility.scenesChanged += SceneImportUtility_scenesChanged;
        }

        static void SceneImportUtility_scenesChanged() =>
            ReinitializeScenePickers();

        static void EditorSceneManager_sceneSaved(UnityEngine.SceneManagement.Scene scene) =>
            ReinitializeScenePickers();

        static void ReinitializeScenePickers()
        {
            foreach (var callback in callbacksOnSceneSaved.Values.ToArray())
                callback.Invoke();
        }

        const string additiveClosed = "";
        const string additiveOpen = "";

        public static void AddButtons(this VisualElement parent, Func<Scene> sceneFunc, int insertAt = 0) =>
            AddButtons(parent, sceneFunc, out _, out _, out _, insertAt);

        public static void AddButtons(this VisualElement parent, Func<Scene> sceneFunc, out Action refresh, int insertAt = 0) =>
            AddButtons(parent, sceneFunc, out _, out _, out refresh, insertAt);

        public static void AddButtons(this VisualElement parent, Func<Scene> sceneFunc, out Button buttonOpen, out Button buttonAdditive, out Action refresh, int insertAt = 0)
        {

            buttonOpen = new Button(OpenScene) { text = "", tooltip = "Open scene", name = "button-open" };
            buttonAdditive = new Button(OpenSceneAdditive) { text = additiveClosed, tooltip = "Open scene additively", name = "button-additive" };

            buttonOpen.AddToClassList("scene-open-button");
            buttonAdditive.AddToClassList("scene-open-button");
            buttonOpen.AddToClassList("fontAwesome");
            buttonAdditive.AddToClassList("fontAwesome");

            parent.Insert(insertAt, buttonOpen);
            parent.Insert(insertAt + 1, buttonAdditive);

            var b1 = buttonOpen;
            var b2 = buttonAdditive;
            refresh = () => RefreshButtons(b1, b2);
            EditorApplication.delayCall += refresh.Invoke;

            SceneManager.runtime.startedWorking += refresh;
            SceneManager.runtime.stoppedWorking += refresh;

            void OpenScene()
            {
#if COROUTINES
                if (sceneFunc.Invoke() is Scene scene && scene)
                    SceneManager.runtime.CloseAll().Open(scene);
#else
                DependencyInjection.DependencyInjectionUtility.GetService<UI.Notifications.EditorCoroutinesNotification>().Show();
#endif
            }

            void OpenSceneAdditive()
            {
#if COROUTINES
                if (sceneFunc.Invoke() is Scene scene && scene)
                    SceneManager.runtime.ToggleOpen(scene);
#else
                DependencyInjection.DependencyInjectionUtility.GetService<UI.Notifications.EditorCoroutinesNotification>().Show();
#endif

            }

            void RefreshButtons(Button buttonOpen, Button buttonAdditive)
            {

                var scene = sceneFunc.Invoke();

                buttonOpen?.SetEnabled(scene && !SceneManager.runtime.isBusy);
                buttonAdditive?.SetEnabled(scene && !SceneManager.runtime.isBusy);

#if !COROUTINES
                buttonOpen.tooltip = "Editor coroutines needed to use this feature.";
                buttonAdditive.tooltip = "Editor coroutines needed to use this feature.";
#endif

                buttonAdditive.text = scene && scene.isOpen ? additiveOpen : additiveClosed;

            }

        }

        static readonly Dictionary<DropdownField, Action> callbacksOnSceneSaved = new();
        public static void SetupSceneDropdown(this DropdownField dropdown, Func<IEnumerable<Scene>> getScenes, Func<Scene> getValue, Action<Scene> setValue, Action onRefreshButton = null, bool allowNull = true) =>
            _SetupSceneDropdown(dropdown, getScenes, getValue, setValue, onRefreshButton, allowNull);

        static void _SetupSceneDropdown(this DropdownField dropdown, Func<IEnumerable<Scene>> getScenes, Func<Scene> getValue, Action<Scene> setValue, Action onRefreshButton = null, bool allowNull = true, Action refreshButtonsCallback = null, Button refreshButton = null)
        {

            var isSetup = false;
            //Setup callback to refresh open buttons, whatever they should be enabled or not.
            if (refreshButtonsCallback == null)
            {
                dropdown.RegisterValueChangedCallback(OnValueChanged);
                AddButtons(dropdown, () => getScenes().ElementAtOrDefault(dropdown.index - (allowNull ? 1 : 0)), out refreshButtonsCallback, 1);
                dropdown.RegisterCallback<DetachFromPanelEvent>(e => callbacksOnSceneSaved.Remove(dropdown));
                callbacksOnSceneSaved.Set(dropdown, Reload);
            }

            if (refreshButton is null && onRefreshButton is not null)
            {
                refreshButton = new Button(() =>
                {
                    onRefreshButton.Invoke();
                    Reload();
                })
                { text = "", tooltip = "Refresh scenes" };

                refreshButton.AddToClassList("scene-open-button");
                refreshButton.AddToClassList("fontAwesome");
                refreshButton.style.marginRight = 3;
                dropdown.Insert(3, refreshButton);
            }

            void Reload() =>
                _SetupSceneDropdown(dropdown, getScenes, getValue, setValue, onRefreshButton, allowNull, refreshButtonsCallback, refreshButton);

            var scenes = getScenes().ToList();
            dropdown.Q(className: "unity-base-field__input").SetEnabled(scenes.Count > 0);

            dropdown.choices = scenes.NonNull().Select(s => s.name).ToList();
            dropdown.index = scenes.IndexOf(getValue());
            dropdown.Q(className: "unity-popup-field__input").tooltip = dropdown.value;
            dropdown.Q(className: "unity-popup-field__input").pickingMode = PickingMode.Position;

            if (allowNull)
            {
                dropdown.choices.Insert(0, "None");
                dropdown.index += 1;
            }

            isSetup = true;

            void OnValueChanged(ChangeEvent<string> e)
            {

                if (EditorApplication.isCompiling || !isSetup)
                    return;

                var i = dropdown.index;
                var scene = getScenes().ElementAtOrDefault(i - (allowNull ? 1 : 0));
                if (getValue() != scene)
                    setValue(scene);

                dropdown.Q(className: "unity-popup-field__input").tooltip = dropdown.value;
                EditorApplication.delayCall += refreshButtonsCallback.Invoke;

            }

        }

    }

}
