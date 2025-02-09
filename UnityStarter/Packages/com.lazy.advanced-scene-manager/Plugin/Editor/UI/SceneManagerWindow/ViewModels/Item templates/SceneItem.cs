using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.ItemTemplates
{

    class SceneItem : ExtendableViewModel
    {

        public override bool IsExtendableButtonsEnabled => true;
        public override ElementLocation Location => ElementLocation.Scene;
        public override VisualElement ExtendableButtonContainer => view.Q("extendable-button-container");
        public override bool IsOverflow => false;
        public override ScriptableObject ExtendableButtonModel => scene;

        public ISceneCollection collection { get; private set; }
        public Scene scene { get; private set; }
        public string path { get; private set; }
        public int index { get; private set; }

        public SceneItem(ISceneCollection collection, Scene scene, int index)
        {
            this.collection = collection;
            this.scene = scene;
            this.index = index;
        }

        public SceneItem(ISceneCollection collection, string scenePath, int index)
        {
            this.collection = collection;
            this.index = index;
            path = scenePath;
        }

        public override void OnAdded()
        {

            base.OnAdded();

            collection.PropertyChanged += Collection_PropertyChanged;

            if (collection is DynamicCollection)
                SetupSceneAssetField();
            else
                ChangeScene(scene);

            SetupRemove();
            SetupMenu();
            SetupContextMenu();
            SetupSelection();


            if (collection is SceneCollection c)
                view.SetupLockBindings(c);

        }

        public override void ApplyAppearanceSettings(VisualElement element)
        {
            element?.Q("button-remove").SetVisible(collection is SceneCollection or StandaloneCollection);
            element?.Q("label-reorder-scene")?.SetVisible(collection is SceneCollection or StandaloneCollection && !search.isSearching);
        }

        public override void OnRemoved()
        {
            collection.PropertyChanged -= Collection_PropertyChanged;
            if (scene)
                scene.PropertyChanged -= Scene_PropertyChanged;
        }

        void ChangeScene(Scene scene)
        {

            if (this.scene)
                this.scene.PropertyChanged -= Scene_PropertyChanged;

            this.scene = scene;
            this.path = "";

            if (scene)
            {
                view.Bind(new(scene));
                scene.PropertyChanged += Scene_PropertyChanged;
                path = scene.path;
            }

            SetupSceneField();

            SetupSceneLoaderIndicator();
            SetupIndicators();

            view.Q<Button>("button-menu").SetEnabled(scene);
            SetupMenu();

            if (collection is SceneCollection c)
                view.Q("scene").EnableInClassList("duplicate", c.scenes.Count(s => s == scene) > 1);

        }

        void Scene_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SetupSceneLoaderIndicator();
            SetupIndicators();
        }

        void Collection_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SetupIndicators();
        }

        void SetupRemove()
        {
            if (collection is ISceneCollection.IEditable c)
                view.Q<Button>("button-remove").clicked += () => c.RemoveAt(index);
        }

        SceneField field;
        EventCallback<ChangeEvent<Scene>> callback;
        void SetupSceneField()
        {

            field = view.Q<SceneField>("field-scene");
            field.SetVisible(true);
            field.SetValueWithoutNotify(scene);
            field.SetObjectPickerEnabled(collection is ISceneCollection.IEditable);

            if (collection is ISceneCollection.IEditable c)
            {

                if (callback is not null)
                    field.UnregisterValueChangedCallback(callback);
                field.RegisterValueChangedCallback(callback = OnFieldChanged);

                void OnFieldChanged(ChangeEvent<Scene> e)
                {
                    c.Replace(index, e.newValue);
                    ChangeScene(e.newValue);
                }

            }

        }

        void SetupSceneAssetField()
        {

            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

            var field = view.Q<ObjectField>("field-sceneAsset");
            field.SetVisible(true);
            field.SetEnabled(false);
            field.SetValueWithoutNotify(asset);
            SetupIndicators();

        }

        void SetupSceneLoaderIndicator()
        {

            var text = view.Q<Label>("label-scene-loader-indicator");
            text.SetVisible(false);

            if (scene && scene.GetSceneLoader() is SceneLoader loader)
            {

                try
                {

                    if (string.IsNullOrWhiteSpace(loader.indicator.text))
                        return;

                    text.text = loader.indicator.text;
                    text.tooltip = string.IsNullOrWhiteSpace(loader.indicator.tooltip) ? loader.sceneToggleText : loader.indicator.tooltip;
                    text.EnableInClassList("fontAwesome", loader.indicator.useFontAwesome);
                    text.EnableInClassList("fontAwesomeBrands", loader.indicator.useFontAwesomeBrands);
                    text.style.color = loader.indicator.color ?? Color.white;
                    text.SetVisible(true);

                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }

            }

        }

        void SetupIndicators()
        {

            var isDoNotOpen = false;
            var isPersistent = scene && scene.keepOpenWhenCollectionsClose;
            if (collection is SceneCollection c && c)
            {
                isDoNotOpen = c.scenesThatShouldNotAutomaticallyOpen.Contains(scene);
                isPersistent = scene && scene.EvalOpenAsPersistent(c, null);
            }

            view.Q("label-do-not-open-indicator").SetVisible(isDoNotOpen);
            view.Q("label-persistent-indicator").SetVisible(isPersistent);

        }

        void SetupMenu()
        {

            var buttonMenu = view.Q<Button>("button-menu");
            var buttonCreate = view.Q<Button>("button-create");

            buttonMenu.clicked -= OpenPopup;
            buttonCreate.clicked -= CreateScene;
            buttonMenu.SetVisible(false);
            buttonCreate.SetVisible(false);

            if (collection is not SceneCollection and not StandaloneCollection)
                return;

            if (scene)
            {
                buttonMenu.clicked += OpenPopup;
                buttonMenu.SetVisible(true);
            }
            else
            {
                buttonCreate.clicked += CreateScene;
                buttonCreate.SetVisible(true);
            }

            void OpenPopup() =>
                OpenPopup<ScenePopup>(new ValueTuple<Scene, ISceneCollection>(scene, collection));

            void CreateScene()
            {

                pickNamePopup.Prompt(async value =>
                {
                    var scene = SceneUtility.CreateAndImport($"{GetCurrentFolderInProjectWindow()}/{value}.unity");
                    field.value = scene;
                    await Task.Delay(250);
                    popupView.ClosePopup();
                },
                onCancel: () => popupView.ClosePopup());

            }

        }

        string GetCurrentFolderInProjectWindow()
        {
            var projectWindowUtilType = typeof(ProjectWindowUtil);
            var getActiveFolderPath = projectWindowUtilType.GetMethod("GetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);
            return getActiveFolderPath.Invoke(null, null) as string;
        }

        void SetupContextMenu()
        {

            if (collection is not SceneCollection c)
                return;

            view.RegisterCallback<ContextClickEvent>(e =>
            {

                var menu = new GenericMenu();
                e.StopPropagation();

                var wasAdded = selection.IsSelected(this);
                var scenes = selection.scenes.ToList();
                if (!wasAdded)
                    scenes.Add(new() { collection = c, sceneIndex = index });

                var distinctScenes = scenes.
                    Select(c => c.collection.scenes.ElementAtOrDefault(c.sceneIndex)).
                    NonNull().
                    Where(s => s).
                    ToArray();

                GenerateSceneHeader(scenes);

                var asset = scene ? (SceneAsset)scene : null;
                if (asset)
                    menu.AddItem(new("View in project view..."), false, () => ContextualActions.ViewInProjectView(asset));
                else
                    menu.AddDisabledItem(new("View in project view..."));

                menu.AddSeparator("");

                if (scene)
                    menu.AddItem(new("Open..."), false, () => ContextualActions.Open(distinctScenes, additive: false));
                else
                    menu.AddDisabledItem(new("Open..."));

                if (scene)
                    menu.AddItem(new("Open additive..."), false, () => ContextualActions.Open(distinctScenes, additive: true));
                else
                    menu.AddDisabledItem(new("Open additive..."));

                menu.AddSeparator("");
                menu.AddItem(new("Remove..."), false, () => ContextualActions.Remove(scenes));

                if (distinctScenes.Length > 1)
                {

                    menu.AddSeparator("");

                    menu.AddItem(new("Merge scenes..."), false, () => ContextualActions.MergeScenes(distinctScenes));
                    menu.AddItem(new("Bake lightmaps..."), false, () => ContextualActions.BakeLightmaps(distinctScenes));

                }

                menu.ShowAsContext();

                void GenerateSceneHeader(IEnumerable<CollectionScenePair> items)
                {

                    var groupedItems = items.GroupBy(i => i.collection).Select(g => (collection: g.Key, scenes: g.Select(i => i.sceneIndex).ToArray()));

                    foreach (var c in groupedItems)
                    {
                        menu.AddDisabledItem(new(c.collection.title));
                        foreach (var index in c.scenes)
                        {
                            var scene = collection.ElementAtOrDefault(index);
                            menu.AddDisabledItem(new(index + ": " + (scene ? scene.name : "none")), false);
                        }
                        menu.AddSeparator("");
                    }
                }


            });

        }

        void SetupSelection()
        {

            var container = view.Q("scene");
            var sceneField = view.Q<SceneField>();

            sceneField.OnClickCallback(e =>
            {

                if (e.button == 0 && (e.ctrlKey || e.commandKey))
                {
                    e.StopPropagation();
                    e.StopImmediatePropagation();
                    selection.ToggleSelection(this);
                    UpdateSelection();
                }

            });

            UpdateSelection();
            void UpdateSelection() =>
                container.EnableInClassList("selected", selection.IsSelected(this));

        }

    }

}
