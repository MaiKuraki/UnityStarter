using System;
using System.ComponentModel;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class ScenePopup : ExtendableViewModel, IPopup
    {

        public override bool IsExtendableButtonsEnabled => !isInspectorWindow;
        public override ElementLocation Location => ElementLocation.Scene;
        public override VisualElement ExtendableButtonContainer => view?.Q("extendable-button-container");
        public override bool IsOverflow => true;

        public string savedSceneID
        {
            get => Get(string.Empty);
            set => Set(value);
        }

        public string savedCollectionID
        {
            get => Get(string.Empty);
            set => Set(value);
        }

        public bool savedIsStandalone
        {
            get => Get(false);
            set => Set(value);
        }

        public bool isInspectorWindow { get; set; }

        Scene scene;
        ISceneCollection collection;

        public override void PassParameter(object parameter)
        {
            if (parameter is (Scene scene, ISceneCollection collection) && scene && collection != null)
            {

                this.scene = scene;
                this.collection = collection;

                if (!isInspectorWindow)
                {
                    savedSceneID = scene.id;
                    savedIsStandalone = collection == Profile.current.standaloneScenes;
                    savedCollectionID = savedIsStandalone ? null : collection.id;
                }

            }
            else
                throw new ArgumentException("Bad parameter. Must be: (Scene, ISceneCollection).");
        }

        public override void OnReopen()
        {
            scene = Scene.Find(savedSceneID);
            collection = savedIsStandalone ? Profile.current.standaloneScenes : SceneCollection.Find(savedCollectionID);
        }

        public override void OnAdded()
        {

            if (!scene || collection == null)
            {
                ClosePopup();
                return;
            }

            base.OnAdded();

            view.Bind(new(scene));

            SetupHeader();
            SetupCollectionOptions();
            SetupStandaloneOptions();
            SetupSceneLoaderToggles();
            SetupSceneOptions();
            SetupEditorOptions();
            SetupLoadPriority();
            SetupEvents();

            view.Q<ScrollView>().PersistScrollPosition();

        }

        public override void OnRemoved()
        {

            if (isInspectorWindow)
                return;

            base.OnRemoved();

            savedSceneID = null;
            savedCollectionID = null;
            savedIsStandalone = false;

            if (collection is SceneCollection c)
                c.Save();
            else if (collection is StandaloneCollection)
                Profile.current.Save();

            if (scene)
                scene.Save();

            view.Q<ScrollView>().ClearScrollPosition();

        }

        #region SceneCollection

        void SetupCollectionOptions()
        {

            if (collection is SceneCollection c)
            {

                view.Q("group-collection").SetVisible(true);
                var toggle = view.Q<Toggle>("toggle-dontOpen");
                toggle.SetValueWithoutNotify(!c.AutomaticallyOpenScene(scene));
                toggle.RegisterValueChangedCallback(e => c.AutomaticallyOpenScene(scene, !e.newValue));

                view.Q<Label>("text-collection-title").text = c.title;

            }

        }

        #endregion
        #region Standalone

        void SetupStandaloneOptions()
        {

            view.Q("group-standalone").SetVisible(collection is StandaloneCollection);
            view.Q<InputBindingField>().Bind(new(scene));

            var list = view.Q<ListView>("list-input-bindings-ignore");
            list.makeItem += () =>
            {
                var container = new VisualElement();
                var field = new SceneField();
                field.style.paddingTop = 0;
                field.style.paddingBottom = 0;
                field.style.paddingLeft = 2;
                field.style.paddingRight = 2;
                container.Add(field);
                return container;
            };

            var dropdown = view.Q<DropdownField>("dropdown-input-loading-scene");
            dropdown.
                SetupSceneDropdown(
                getScenes: () => Assets.scenes.Where(s => s.isLoadingScreen),
                getValue: () => scene.inputBindingsLoadingScene,
                setValue: (s) =>
                {
                    scene.inputBindingsLoadingScene = s;
                    scene.Save();
                },
                onRefreshButton: () => LoadingScreenUtility.RefreshSpecialScenes());
        }

        #endregion
        #region Scene

        void SetupHeader()
        {

            view.Q<Label>("text-title").text = scene.name;
            view.Q<Button>("button-close").clicked += ClosePopup;

            view.Q<Button>("button-rename").clicked += () =>
                pickNamePopup.Prompt(
                    value: scene.name,
                    onContinue: (title) =>
                    {
                        AssetDatabase.RenameAsset(scene.path, title);
                        EditorApplication.delayCall += () => OpenPopup<ScenePopup>((scene, collection));
                    },
                    onCancel: () => OpenPopup<ScenePopup>((scene, collection)));

        }

        void SetupSceneOptions()
        {
            SetupSceneLoaderToggles();
            SetupHalfPersistent();
        }

        void SetupSceneLoaderToggles()
        {

            var list = view.Q("group-scene-loader-toggles");
            scene.PropertyChanged += OnPropertyChanged;
            view.RegisterCallback<DetachFromPanelEvent>(e => scene.PropertyChanged -= OnPropertyChanged);

            void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(scene.sceneLoader))
                    Reload();
            }

            Reload();

            void Reload()
            {

                list.Clear();

                foreach (var loader in SceneManager.runtime.GetToggleableSceneLoaders().OrderBy(l => l.Key).ToArray())
                {

                    var button = new Toggle { label = loader.sceneToggleText };
                    button.SetValueWithoutNotify(scene.sceneLoader == loader.Key);
                    button.RegisterValueChangedCallback(e =>
                    {

                        if (e.newValue)
                            scene.sceneLoader = loader.Key;
                        else
                            scene.ClearSceneLoader();

                        scene.Save();

                    });

                    list.Add(button);

                }

            }

        }

        void SetupHalfPersistent()
        {

            const int ReopenIndex = 0;
            const int RemainOpenIndex = 1;

            var dropdown = view.Q<DropdownField>("dropdown-half-persistent");
            dropdown.index = scene.keepOpenWhenNewCollectionWouldReopen ? RemainOpenIndex : ReopenIndex;
            dropdown.RegisterValueChangedCallback(e => scene.keepOpenWhenNewCollectionWouldReopen = dropdown.index == RemainOpenIndex);

        }

        void SetupLoadPriority()
        {

            view.Q("enum-loading-priority").Bind(new(scene));

            var descriptionCollapsed = view.Q("description-collapsed");
            var descriptionExpanded = view.Q("description-expanded");

            descriptionCollapsed.Q<Button>().clicked += () => Toggle(expanded: true);
            descriptionExpanded.Q<Button>().clicked += () => Toggle(expanded: false);

            void Toggle(bool expanded)
            {
                descriptionCollapsed.SetVisible(!expanded);
                descriptionExpanded.SetVisible(expanded);
            }

        }

        #endregion
        #region Editor

        void SetupEditorOptions()
        {

#if UNITY_2022_1_OR_NEWER

            var list = view.Q<ListView>("list-auto-open-scenes");
            var enumField = view.Q<UnityEngine.UIElements.EnumField>("enum-auto-open-in-editor");
            list.makeItem = () => new SceneField();

            list.bindItem = (element, i) =>
            {

                var field = (SceneField)element;

                if (scene.autoOpenInEditorScenes.ElementAtOrDefault(i) is Scene s && s)
                    field.SetValueWithoutNotify(s);
                else
                    field.SetValueWithoutNotify(null);

                field.RegisterValueChangedCallback(e => scene.autoOpenInEditorScenes[i] = e.newValue);

            };

            enumField.RegisterValueChangedCallback(e => UpdateListVisible());

            UpdateListVisible();
            void UpdateListVisible() =>
                list.SetVisible(scene.autoOpenInEditor == Models.Enums.EditorPersistentOption.WhenAnyOfTheFollowingScenesAreOpened);

#endif

        }

        #endregion
        #region Events

        [Flags]
        public enum EventGroups
        {
            None = 0,
            OnOpened = 1 << 0,          // 1
            OnClosed = 1 << 1,         // 2
            OnPreloaded = 1 << 2,       // 4
            OnPreloadFinished = 1 << 3, // 8
            OnCollectionOpened = 1 << 4, // 16
            OnCollectionClosed = 1 << 5 // 32
        }

        EventGroups isEventsExpanded
        {
            get => Get(EventGroups.None);
            set => Set(value);
        }

        void SetupEvents()
        {

            var group = view.Q("group-events");

            //Save and restore expanded state
            SetupEventGroup(group, EventGroups.OnOpened);
            SetupEventGroup(group, EventGroups.OnClosed);
            SetupEventGroup(group, EventGroups.OnPreloaded);
            SetupEventGroup(group, EventGroups.OnPreloadFinished);
            SetupEventGroup(group, EventGroups.OnCollectionOpened);
            SetupEventGroup(group, EventGroups.OnCollectionClosed);

            //We have custom styling which will make elements expand outside normal PropertyField height
            group.Query<ListView>().ForEach(element => element.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight);

        }

        void SetupEventGroup(VisualElement view, EventGroups group)
        {

            var foldout = view.Q<Foldout>("group-event-" + group.ToString());
            foldout.value = isEventsExpanded.HasFlag(group);

            foldout.RegisterValueChangedCallback(e =>
            {
                if (e.newValue)
                    isEventsExpanded |= group;
                else
                    isEventsExpanded &= group;
            });

        }

        #endregion

    }

}

