using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views.ItemTemplates;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views
{

    class CollectionView : ViewModel, IView
    {

        ListView collectionsList;
        ListView dynamicCollectionsList;
        VisualElement standaloneList;
        ScrollView scroll;

        public VisualTreeAsset collectionTemplate => SceneManagerWindow.viewLocator.items.collection;

        public override void OnAdded()
        {
            Profile.onProfileChanged += Reload;
            Reload();
            scroll.PersistScrollPosition();
        }

        public override void OnRemoved()
        {
            Profile.current.PropertyChanged -= Profile_PropertyChanged;
            Profile.onProfileChanged -= Reload;
            SceneImportUtility.scenesChanged -= Reload;
            scroll.ClearScrollPosition();
        }

        private void Profile_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Profile.defaultASMScenes))
                Reload();
        }

        public void Reload()
        {
            if (Profile.current)
            {
                Profile.current.PropertyChanged -= Profile_PropertyChanged;
                Profile.current.PropertyChanged += Profile_PropertyChanged;
            }

            collectionsList = view.Q<ListView>("list-collections") ?? throw new InvalidOperationException("Could not find collections list");
            dynamicCollectionsList = view.Q<ListView>("list-dynamic-collections") ?? throw new InvalidOperationException("Could not find dynamic collections list"); ;
            standaloneList = view.Q("list-standalone") ?? throw new InvalidOperationException("Could not find standalone collections list");
            scroll = view.Q<ScrollView>() ?? throw new InvalidOperationException("Could not find collection scrollview");

            standaloneList.Clear();
            collectionsList.Clear();
            dynamicCollectionsList.Clear();
            hasCollection = false;
            UpdateNoItemsMessage();

            SetupCollectionList<SceneCollection>(collectionsList);
            SetupCollectionList<DynamicCollection>(dynamicCollectionsList);

            if (Profile.current)
            {
                SetupSingleCollection(Profile.current.standaloneScenes);
#if ASM_DEV
                if (Profile.current.m_displayDefaultASMScenes)
                    SetupSingleCollection(Profile.current.defaultASMScenes);
#endif
            }

            SetupList(collectionsList);
            SetupList(dynamicCollectionsList);

            SetupNoProfileMessage();
            SetupLine();

            EditorApplication.delayCall += () => ApplyAppearanceSettings(view);

        }

        bool hasCollection;
        public void UpdateSeparator()
        {

            var c = SceneManager.assets.collections.LastOrDefault();
            if (c && collectionsList is not null)
                collectionsList.style.marginBottom =
                     hasCollection && SceneManager.settings.user.m_expandedCollections.Contains(c.id)
                    ? 0
                    : 8;

        }

        void SetupList(ListView list)
        {

            //Both lists should use the same scrollview, so lets disable the lists own internal scrollview
            list.Q<ScrollView>().verticalScrollerVisibility = ScrollerVisibility.Hidden;

            //We use padding-bottom to give some space for expanded collections, this prevents that on last item in list
            list.Query<TemplateContainer>().Last()?.AddToClassList("last");

            list.itemIndexChanged -= OnItemIndexChanged;
            list.itemIndexChanged += OnItemIndexChanged;

            void OnItemIndexChanged(int oldIndex, int newIndex) =>
              EditorApplication.delayCall += collectionView.Reload;

        }

        Label noItemsLabel;
        void UpdateNoItemsMessage()
        {
            noItemsLabel = view.Q<Label>("label-no-items");
            noItemsLabel.text = search.isSearching ? "No items found." : "No collections added, you can add one below!";
            noItemsLabel.SetVisible(Profile.current && !hasCollection);
        }

        void SetupNoProfileMessage() => view.Q("label-no-profile").visible = !SceneManager.profile;
        void SetupLine()
        {
            view.Q("line").visible = SceneManager.profile;
            UpdateSeparator();
        }

        void SetupCollectionList<T>(ListView list) where T : ISceneCollection
        {

            list.makeItem = () => Instantiate(collectionTemplate);

            //Make sure that list is fully reset
            list.Unbind();
            list.itemsSource = Array.Empty<SceneCollection>();
            list.bindItem = null;
            list.Rebuild();

            if (!Profile.current)
                return;

            if (typeof(T) == typeof(SceneCollection))
            {

                //Search view
                if (search.isSearching && search.savedSearch != null)
                {

                    var items = search.savedSearch.Keys.ToList();

                    list.itemsSource = items;
                    list.bindItem = (element, index) =>
                    {
                        if (items.ElementAtOrDefault(index) is SceneCollection c && c.hasID)
                        {
                            hasCollection = true;
                            UpdateSeparator();
                            UpdateNoItemsMessage();
                            OnSetupCollection(element, c);
                        }
                        else
                            element.RemoveFromHierarchy();
                    };
                }
                else
                {

                    //Normal collection view
                    list.bindItem = (element, index) =>
                    {
                        if (Profile.current && Profile.current.collections.ElementAtOrDefault(index) is SceneCollection c && c && c.hasID)
                        {
                            hasCollection = true;
                            UpdateSeparator();
                            UpdateNoItemsMessage();
                            OnSetupCollection(element, c);
                        }
                        else
                            element.RemoveFromHierarchy();
                    };

                    var property = Profile.serializedObject.FindProperty("m_collections");
                    list.BindProperty(property);

                }

            }
            else if (typeof(T) == typeof(DynamicCollection))
            {

                //Dynamic collection view
                list.bindItem = (element, index) =>
                {
                    if (Profile.current && Profile.current.dynamicCollections.ElementAtOrDefault(index) is DynamicCollection c && c.hasID)
                        OnSetupCollection(element, c);
                    else
                        element.RemoveFromHierarchy();
                };

                var property = Profile.serializedObject.FindProperty("m_dynamicCollections");
                list.BindProperty(property);

            }

            list.Rebuild();

        }

        void SetupSingleCollection(ISceneCollection collection)
        {

            var element = collectionTemplate.Instantiate();
            standaloneList.Add(element);
            OnSetupCollection(element, collection);

        }

        public readonly Dictionary<ISceneCollection, ViewModel> views = new();
        void OnSetupCollection(VisualElement element, ISceneCollection collection)
        {

            if (collection is null)
                return;

            element.RegisterCallback<DetachFromPanelEvent>(e =>
            {
                if (views.Remove(collection, out var item))
                    item.InvokeView(item.OnRemoved);
            });

            var view = views.Set(collection, new CollectionItem(collection));
            view.SetView(element);
            view.InvokeView(view.OnAdded);

        }

        public override void ApplyAppearanceSettings(VisualElement element)
        {
            foreach (var view in views)
                view.Value.ApplyAppearanceSettings(view.Value.view);
        }

    }

}
