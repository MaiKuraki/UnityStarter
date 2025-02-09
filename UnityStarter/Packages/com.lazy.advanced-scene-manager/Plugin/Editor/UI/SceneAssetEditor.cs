using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine.UIElements;
using static AdvancedSceneManager.Editor.Utility.SceneImportUtility.StringExtensions;

namespace AdvancedSceneManager.Editor
{

    [CustomEditor(typeof(SceneAsset))]
    class SceneAssetEditor : UnityEditor.Editor
    {

        #region Scene popup

        readonly List<ScenePopup> scenePopups = new();
        void AddScenePopup(VisualElement element, ISceneCollection collection = null)
        {

            var popup = new ScenePopup() { isInspectorWindow = true };
            scenePopups.Add(popup);

            var template = SceneManagerWindow.viewLocator.popups.scene;

            var view = template.Instantiate();
            element.Add(view);
            popup.SetView(view);
            popup.PassParameter((scene, collection ?? Profile.current.standaloneScenes));
            popup.OnAdded();

            view.Q("group-header").Hide();

            if (collection is null)
            {
                view.Q("group-collection").Hide();
                view.Q("group-standalone").Hide();
            }
            else
            {

                view.Query<GroupBox>().ForEach(e => e.Hide());

                var group =
                    collection == Profile.current.standaloneScenes
                    ? view.Q("group-standalone")
                    : view.Q("group-collection");

                var removeButton = new Button() { text = "Remove" };
                removeButton.clicked += () => ((ISceneCollection.IEditable)collection).Remove(scene);
                group.Add(removeButton);
                removeButton.style.position = Position.Absolute;
                removeButton.style.right = 12;

                removeButton.Hide();

                group.RegisterCallback<PointerEnterEvent>(e => removeButton.Show());
                group.RegisterCallback<PointerLeaveEvent>(e => removeButton.Hide());

                if (collection is StandaloneCollection)
                    view.Q("group-input-bindings").Show();

                group.Show();

            }

        }

        void RemoveScenePopup()
        {

            foreach (var popup in scenePopups)
            {
                if (popup?.view is not null)
                {
                    popup.OnRemoved();
                    popup.SetView(null);
                }
            }

            scenePopups.Clear();

        }

        #endregion

        SceneAsset sceneAsset;
        Scene scene;
        string path;

        void OnEnable()
        {
            SceneImportUtility.scenesChanged += Reload;
        }

        void OnDisable()
        {
            SceneImportUtility.scenesChanged -= Reload;
            RemoveScenePopup();
        }

        VisualElement rootVisualElement;
        public override VisualElement CreateInspectorGUI()
        {

            rootVisualElement = new VisualElement();

            rootVisualElement.style.marginTop = 4;
            rootVisualElement.style.marginBottom = 4;
            rootVisualElement.style.marginLeft = -8;

            rootVisualElement.RegisterCallbackOnce<AttachToPanelEvent>(e =>
            {
                foreach (var style in SceneManagerWindow.FindStyles())
                    rootVisualElement.styleSheets.Add(style);
            });

            Reload();

            return rootVisualElement;

        }

        void Reload()
        {

            if (rootVisualElement is null)
                return;

            sceneAsset = (SceneAsset)target;
            path = AssetDatabase.GetAssetPath(sceneAsset);
            _ = SceneImportUtility.GetImportedScene(path, out scene);

            RemoveScenePopup();
            rootVisualElement.Clear();

            if (FallbackSceneUtility.GetStartupScene() == path)
                FallbackScene(rootVisualElement);
            else if (!scene)
                Unimported(rootVisualElement);
            else if (scene.isImported)
                ASMScene(rootVisualElement);
            else
                ImportedScene(rootVisualElement);

        }

        void FallbackScene(VisualElement element)
        {
            element.Add(new HelpBox(" This scene is designated as ASM fallback scene.", HelpBoxMessageType.Info));
        }

        void Unimported(VisualElement element)
        {

            if (BlocklistUtility.IsBlacklisted(path))
                element.Add(new HelpBox(" This scene has been blacklisted for import into Advanced Scene Manager. It cannot be imported", HelpBoxMessageType.Info));
            else if (!IsValidSceneToImport(path))
            {

                var isScene = "IsScene: " + IsScene(path);
                var isNotImported = "IsNotImported: " + !IsImported(path);
                var isNotBlacklisted = "IsNotBlacklisted: " + !IsBlacklisted(path);
                var isNotTestScene = "IsNotTestScene: " + !IsTestScene(path);
                var isNotPackageScene = "IsNotPackageScene: " + !IsPackageScene(path);
                var isNotASMScene = "IsNotASMScene: " + !IsASMScene(path);

                var str = string.Join("\n ", isScene, isNotImported, isNotBlacklisted, isNotTestScene, isNotPackageScene, isNotASMScene);

                element.Add(new HelpBox(" This scene is not valid to import in Advanced Scene Manager.\n One of the following filters may indicate why:\n " + str, HelpBoxMessageType.Info));

            }
            else
            {
                var button = new Button() { text = "Import into ASM" };
                button.clicked += () => SceneImportUtility.Import(path);
                button.style.alignSelf = Align.FlexEnd;
                element.Add(button);
            }

        }

        void ASMScene(VisualElement element)
        {

            if (scene == SceneManager.assets.defaults.inGameToolbarScene)
                element.Add(new HelpBox(" " + Scene.InGameToolbarDescription, HelpBoxMessageType.Info));
            else if (scene == SceneManager.assets.defaults.pauseScene)
                element.Add(new HelpBox(" " + Scene.PauseScreenDescription, HelpBoxMessageType.Info));

            ImportedScene(element);

        }

        void ImportedScene(VisualElement element)
        {

            AddSceneField(element);

            if (!Profile.current)
                return;

            var collections = Profile.current.collections.Where(c => c.Contains(scene)).Cast<ISceneCollection>().ToList();
            if (Profile.current.standaloneScenes.Contains(scene))
                collections.Add(Profile.current.standaloneScenes);

            AddScenePopup(element);

            if (collections.Count == 1)
            {
                AddScenePopup(element, collections.First());
            }
            else if (collections.Count > 1)
            {

                foreach (var collection in collections)
                    AddScenePopup(element, collection);

            }

            AddToButton(element);

        }

        void AddSceneField(VisualElement element)
        {

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.paddingTop = 6;

            var field = new SceneField() { value = scene };
            field.SetObjectPickerEnabled(false);
            field.style.flexGrow = 1;
            field.style.marginRight = 3;

            container.Add(field);

            if (!scene.isDefaultASMScene)
            {

                var button = new Button() { text = "Unimport" };
                button.clicked += () => SceneImportUtility.Unimport(scene);
                button.style.marginLeft = 3;

                container.Add(button);

            }

            element.Add(container);

        }

        void AddToButton(VisualElement element)
        {

            var menuItems = Profile.current.collections.Cast<ISceneCollection>().Concat(Profile.current.standaloneScenes).Select(c => (
                header: c.title,
                action: new Action(() => ((ISceneCollection.IEditable)c).Add(scene)),
                enabled: !c.Contains(scene)
            )).ToArray();

            if (!menuItems.Any(m => m.enabled))
                return;

            var button = new Button() { text = "Add to:" };

            button.style.marginTop = 8;

            element.Add(button);

            button.SetupMenuButton(menuItems);
            button.DisableMenuButtonAutoHide();

        }

    }

}
