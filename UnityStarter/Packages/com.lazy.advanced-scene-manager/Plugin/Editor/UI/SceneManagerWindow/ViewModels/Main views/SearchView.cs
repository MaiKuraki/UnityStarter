using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views
{

    class SearchView : ViewModel, IView
    {

        public bool lastSearchScenes
        {
            get => Get(false);
            set => Set(value);
        }

        public string lastSearch
        {
            get => Get(string.Empty);
            set => Set(value);
        }

        public override void OnAdded()
        {
            Setup();
            profileBindingService.BindEnabledToProfile(rootVisualElement.Q<Button>("button-search"));
        }

        public bool IsInActiveSearch(SceneCollection collection, Scene scene = null)
        {

            if (!isSearching)
                return false;

            return
                lastSearchScenes
                ? (savedSearch.GetValueOrDefault(collection)?.Contains(scene) ?? false)
                : savedSearch.ContainsKey(collection);

        }

        public SerializableDictionary<SceneCollection, Scene[]> savedSearch { get; private set; }

        public void Toggle()
        {
            if (searchGroup.style.display == DisplayStyle.Flex && !shouldAlwaysDisplaySearch)
                Stop();
            else
                Start();
        }

        public void Start()
        {
            searchGroup.SetVisible(true);
            UpdateSaved();
            searchField.Focus();
            UpdateSearchButton();
        }

        public void Stop()
        {

            lastSearch = string.Empty;
            savedSearch = null;
            searchField.SetValueWithoutNotify(string.Empty);
            searchGroup.SetVisible(false);
            collectionView.Reload();
            UpdateSearchButton();

        }

        GroupBox searchGroup;
        TextField searchField;
        RadioButton searchCollections;
        RadioButton searchScenes;
        Button searchButton;

        Button saveButton;
        VisualElement list;

        public bool isSearching => !string.IsNullOrEmpty(lastSearch);
        public bool shouldAlwaysDisplaySearch => Profile.current && SceneManager.settings.user.alwaysDisplaySearch;

        [ASMWindowElement(ElementLocation.Header, isVisibleByDefault: true)]
        static VisualElement OpenSearch()
        {

            var button = new Button(DependencyInjectionUtility.GetService<SearchView>().Toggle)
            {
                name = "button-search",
                text = "",
                tooltip = "Search collections and scenes (ctrl+f can also be used)",
            };
            button.UseFontAwesome(solid: true);

            return button;

        }

        [Shortcut("ASM/Search", typeof(SceneManagerWindow), defaultShortcutModifiers: ShortcutModifiers.Control, defaultKeyCode: KeyCode.F)]
        static void HotKey()
        {

            var view = DependencyInjectionUtility.GetService<SearchView>();

            if (view.popupView.currentPopup is null)
                view.Toggle();

        }

        #region Setup

        void Setup()
        {

            searchGroup = view.Q<GroupBox>("group-search");
            searchField = view.Q<TextField>("text-search");
            placeholder = view.Q("label-placeholder");
            searchCollections = view.Q<RadioButton>("toggle-collections");
            searchScenes = view.Q<RadioButton>("toggle-scenes");
            saveButton = view.Q<Button>("button-save-search");
            list = view.Q("list-saved");
            searchButton = rootVisualElement.Q<Button>("button-search");

            searchGroup.SetVisible(shouldAlwaysDisplaySearch || isSearching);

            if (isSearching && savedSearch == null)
                UpdateSearch(lastSearch, lastSearchScenes, true, false);

            SetupSave();

            SetupToggles();
            SetupPlaceholder();
            SetupSearchBox();
            SetupGroup();

            UpdateSearchButton();
            SceneManager.settings.user.PropertyChanged += (s, e) =>
            {
                searchGroup.SetVisible(shouldAlwaysDisplaySearch);
                UpdateSearchButton();
            };

        }

        void SetupSave()
        {

            UpdateSaved();
            UpdateSaveButton();

            saveButton.clickable = null;
            saveButton.clickable = new(() =>
            {

                if (SceneManager.settings.user.savedSearches.Contains(searchField.text))
                    ArrayUtility.Remove(ref SceneManager.settings.user.savedSearches, searchField.text);
                else
                    ArrayUtility.Add(ref SceneManager.settings.user.savedSearches, searchField.text);

                SceneManager.settings.user.Save();
                UpdateSaved();
                UpdateSaveButton();

            });

            searchField.RegisterValueChangedCallback(e => UpdateSaveButton());

        }

        void SetupToggles()
        {
            UpdateToggles();

            searchCollections.RegisterValueChangedCallback(e => { if (e.newValue) UpdateSearch(lastSearch, false); });
            searchScenes.RegisterValueChangedCallback(e => { if (e.newValue) UpdateSearch(lastSearch, true); });
        }

        VisualElement placeholder;
        void SetupPlaceholder()
        {
            if (placeholder == null)
                UpdatePlaceholder();
        }

        void UpdatePlaceholder()
        {
            placeholder.SetVisible(string.IsNullOrEmpty(searchField.text));
        }

        void SetupSearchBox()
        {
#if UNITY_2022_1_OR_NEWER
            searchField.selectAllOnMouseUp = false;
            searchField.selectAllOnFocus = false;
#endif
            searchField.value = lastSearch;

            UpdatePlaceholder();
            searchField.RegisterValueChangedCallback(e => UpdatePlaceholder());

            searchField.RegisterCallback<KeyUpEvent>(e =>
            {

                if (e.keyCode is KeyCode.KeypadEnter or KeyCode.Return)
                    UpdateSearch(searchField.text, lastSearchScenes);
                else
                    UpdateSearchDelayed();

                UpdateSaveButton();

            });

        }

        void SetupGroup()
        {

            searchGroup.RegisterCallback<PointerDownEvent>(e =>
            {
                if (searchField.panel.focusController.focusedElement != searchField)
                    RefocusSearch(e.clickCount > 2);
            });

            void RefocusSearch(bool isDoubleClick = false)
            {

                searchField.Focus();

                if (isDoubleClick)
                    searchField.SelectAll();
                else
                    searchField.SelectRange(searchField.text.Length, searchField.text.Length);

            }

        }

        #endregion
        #region Search

        System.Timers.Timer timer = null;

        void UpdateSearchDelayed()
        {

            if (timer is null)
            {
                timer = new(500) { Enabled = false };
                timer.Elapsed += (s, e) => EditorApplication.delayCall += () => UpdateSearch(searchField.text, lastSearchScenes);
            }

            timer.Stop();
            timer.Start();

        }

        void UpdateSearch(string q, bool searchScenes, bool force = false, bool reload = true)
        {

            timer?.Stop();

            if (!force && lastSearch == q && lastSearchScenes == searchScenes)
                return;

            lastSearch = q;
            lastSearchScenes = searchScenes;
            UpdateToggles();

            savedSearch =
                !lastSearchScenes
                ? FindCollections(q)
                : FindScenes(q);

            if (reload)
                collectionView.Reload();

        }

        #endregion
        #region Update UI

        void UpdateToggles()
        {
            searchCollections.SetValueWithoutNotify(!lastSearchScenes);
            searchScenes.SetValueWithoutNotify(lastSearchScenes);
        }

        void UpdateSearchButton()
        {
            searchButton.text = shouldAlwaysDisplaySearch ? "" : "";
            searchButton.tooltip = shouldAlwaysDisplaySearch ? "Clear search" : "Search collections and scenes";
        }

        void UpdateSaveButton()
        {

            saveButton.SetVisible(!string.IsNullOrEmpty(searchField.text));
            if (SceneManager.settings.user.savedSearches?.Contains(searchField.text) ?? false)
            {
                saveButton.UseFontAwesome(solid: true);
                saveButton.text = "";
            }
            else
            {
                saveButton.UseFontAwesome(solid: true);
                saveButton.text = "";
            }

        }

        void UpdateSaved()
        {
            list.Clear();
            foreach (var item in SceneManager.settings.user.savedSearches ?? Array.Empty<string>())
                list.Add(new Button(() => { searchField.value = item; UpdateSearch(item, lastSearchScenes); UpdateSaveButton(); }) { text = item, name = "button-saved-search" });
        }

        #endregion
        #region Find

        SerializableDictionary<SceneCollection, Scene[]> FindCollections(string q)
        {

            var dict = new Dictionary<SceneCollection, List<Scene>>();
            foreach (var collection in Profile.current.collections)
                if (collection.title.Contains(q, StringComparison.InvariantCultureIgnoreCase))
                    dict.Add(collection, null);

            var dict2 = new SerializableDictionary<SceneCollection, Scene[]>() { throwOnDeserializeWhenKeyValueMismatch = false };
            foreach (var item in dict)
                dict2.Add(item.Key, null);

            return dict2;

        }

        SerializableDictionary<SceneCollection, Scene[]> FindScenes(string q)
        {

            var dict = new Dictionary<SceneCollection, List<Scene>>();
            foreach (var collection in Profile.current.collections)
                foreach (var scene in collection.scenes.NonNull())
                {
                    if (scene.name.Contains(q, StringComparison.InvariantCultureIgnoreCase))
                        if (dict.ContainsKey(collection))
                            dict[collection].Add(scene);
                        else
                            dict.Add(collection, new() { scene });
                }

            var dict2 = new SerializableDictionary<SceneCollection, Scene[]>() { throwOnDeserializeWhenKeyValueMismatch = false };
            foreach (var item in dict)
                dict2.Add(item.Key, item.Value.ToArray());

            return dict2;

        }

        #endregion

    }

}
