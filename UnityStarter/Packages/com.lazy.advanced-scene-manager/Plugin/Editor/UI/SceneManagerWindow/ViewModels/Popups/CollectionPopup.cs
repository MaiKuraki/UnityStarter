using System;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class CollectionPopup : ExtendableViewModel, IPopup
    {

        public override bool IsExtendableButtonsEnabled => true;
        public override ElementLocation Location => ElementLocation.Collection;
        public override VisualElement ExtendableButtonContainer => view.Q("extendable-button-container");
        public override bool IsOverflow => true;

        string savedCollectionID
        {
            get => Get(string.Empty);
            set => Set(value);
        }

        SceneCollection collection;

        public override void PassParameter(object parameter)
        {
            if (parameter is SceneCollection collection && collection)
                this.collection = collection;
            else
                throw new ArgumentException("Bad parameter. Must be SceneCollection.");
        }

        public override void OnReopen() =>
            collection = SceneCollection.Find(savedCollectionID);

        public override void OnRemoved()
        {
            base.OnRemoved();
            savedCollectionID = null;
            view.Q<ScrollView>().ClearScrollPosition();
        }

        public override void OnAdded()
        {

            if (!collection)
            {
                ClosePopup();
                return;
            }

            base.OnAdded();

            view.Bind(new(collection));
            savedCollectionID = collection.id;

            SetupTitle();
            SetupSceneLoaderToggles();
            SetupLoadingOptions();
            SetupStartupOptions();
            SetupLock();
            SetupActiveScene();
            SetupLoadPriority();
            SetupEvents();

            view.Q<InputBindingField>().Bind(new(collection));

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

            view.Q<Button>("button-close").clicked += ClosePopup;

            view.Q<ScrollView>().PersistScrollPosition();

        }

        #region Title

        void SetupTitle()
        {

            view.Q<Button>("button-rename").clicked += () =>
            {
                pickNamePopup.Prompt(
                    value: collection.title,
                    onContinue: (title) =>
                    {
                        collection.Rename(title);
                        OpenPopup<CollectionPopup>(collection);
                    },
                    onCancel: () => OpenPopup<CollectionPopup>(collection));
            };

        }

        #endregion
        #region Active scene

        void SetupActiveScene() =>
            view.Q<DropdownField>("dropdown-active-scene").
                SetupSceneDropdown(
                getScenes: () => collection.scenes,
                getValue: () => collection.activeScene,
                setValue: (s) =>
                {
                    collection.activeScene = s;
                    collection.Save();
                });

        #endregion
        #region Lock

        void SetupLock()
        {

            var lockButton = view.Q<Button>("button-lock");
            var unlockButton = view.Q<Button>("button-unlock");
            lockButton.clicked += () => collection.Lock(prompt: true);
            unlockButton.clicked += () => collection.Unlock(prompt: true);

            BindingHelper lockBinding = null;
            BindingHelper unlockBinding = null;

            ReloadButtons();
            view.SetupLockBindings(collection);

            void ReloadButtons()
            {

                lockBinding?.Unbind();
                unlockBinding?.Unbind();
                lockButton.SetVisible(false);
                unlockButton.SetVisible(false);

                if (!SceneManager.settings.project.allowCollectionLocking)
                    return;

                lockBinding = lockButton.BindVisibility(collection, nameof(collection.isLocked), true);
                unlockBinding = unlockButton.BindVisibility(collection, nameof(collection.isLocked));

            }

        }

        #endregion
        #region Scene loader toggles

        void SetupSceneLoaderToggles()
        {

            var list = view.Q("group-scene-loader-toggles");

            Reload();

            void Reload()
            {

                list.Clear();

                foreach (var loader in SceneManager.runtime.GetToggleableSceneLoaders().OrderBy(l => l.Key).ToArray())
                {

                    var isCheck = collection.scenes.NonNull().All(s => s.sceneLoader == loader.Key);
                    var isMixedValue = !isCheck && collection.scenes.NonNull().Any(s => s.sceneLoader == loader.Key);

                    var button = new Toggle();
                    button.showMixedValue = isMixedValue;
                    button.label = loader.sceneToggleText;
                    button.SetValueWithoutNotify(isCheck);
                    button.RegisterValueChangedCallback(e =>
                    {
                        foreach (var scene in collection.scenes.NonNull())
                        {
                            if (e.newValue)
                                scene.sceneLoader = loader.Key;
                            else
                                scene.ClearSceneLoader();
                            scene.Save();
                        }
                        Reload();
                    });

                    list.Add(button);

                }

            }

        }

        #endregion
        #region Loading options

        void SetupLoadingOptions()
        {

            var dropdown = view.Q<DropdownField>("dropdown-loading-scene");
            dropdown.
                SetupSceneDropdown(
                getScenes: () => Assets.scenes.Where(s => s.isLoadingScreen),
                getValue: () => collection.loadingScreen,
                setValue: (s) =>
                {
                    collection.loadingScreen = s;
                    collection.Save();
                },
                onRefreshButton: () => LoadingScreenUtility.RefreshSpecialScenes());

            dropdown.SetEnabled(collection.loadingScreenUsage is LoadingScreenUsage.Override);
            _ = view.Q<UnityEngine.UIElements.EnumField>("enum-loading-screen").
                RegisterValueChangedCallback(e =>
                    dropdown.SetEnabled(e.newValue is LoadingScreenUsage.Override));

        }

        #endregion
        #region Startup options

        void SetupStartupOptions()
        {

            var group = view.Q<RadioButtonGroup>("radio-group-startup");
            group.RegisterValueChangedCallback(e => collection.OnPropertyChanged(nameof(collection.startupOption)));
        }

        #endregion
        #region Load priority

        void SetupLoadPriority()
        {

            view.Q("enum-loading-priority").Bind(new(collection));

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
        #region Events

        [Flags]
        public enum EventGroups
        {
            None = 0,
            OnOpened = 1 << 0,          // 1
            OnClosed = 1 << 1,         // 2
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
