using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.DependencyInjection.Editor;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views.ItemTemplates;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views
{

    class SelectionView : ViewModel, IView
    {

        public override void OnAdded()
        {
            SetupUI();
            SetupSelection();
        }

        List<SceneCollection> selectedCollections => SceneManager.settings.user.m_selectedCollections;
        List<CollectionScenePair> selectedScenes => SceneManager.settings.user.m_selectedScenes;

        IEnumerable<Scene> actionableScenes => selectedScenes.Select(s => s.collection[s.sceneIndex]).NonNull();

        bool hasSelection => selectedCollections.Count > 0 || selectedScenes.Count > 0;

        void Save() =>
            SceneManager.settings.user.Save();

        #region UI

        void SetupUI()
        {
            SetupCollectionUI();
            SetupSceneUI();
            UpdateUI();
        }

        void UpdateUI()
        {
            UpdateCollectionUI();
            UpdateSceneUI();
        }

        #region Collection UI

        GroupBox collectionsGroup;
        Label collectionCountLabel;

        Button collectionViewButton;
        Button collectionCreateTemplateButton;

        void SetupCollectionUI()
        {
            collectionsGroup = view.Q<GroupBox>("group-collections");
            collectionCountLabel = collectionsGroup.Q<Label>("count-collections");
            collectionViewButton = collectionsGroup.Q<Button>("button-view");
            collectionCreateTemplateButton = collectionsGroup.Q<Button>("button-create-template");
        }

        void UpdateCollectionUI()
        {

            collectionsGroup.SetVisible(selectedCollections.Count > 0);
            collectionCountLabel.text = selectedCollections.Count().ToString();

            collectionViewButton.SetEnabled(selectedCollections.Count == 1);
            collectionCreateTemplateButton.SetEnabled(selectedCollections.Count == 1);

            collectionViewButton.clicked += () => ContextualActions.ViewInProjectView(selectedCollections.First());
            collectionCreateTemplateButton.clicked += () => ContextualActions.CreateTemplate(selectedCollections.First());

            collectionsGroup.Q<Button>("button-remove").clicked += () => ContextualActions.Remove(selectedCollections);

        }

        #endregion
        #region Scene

        GroupBox scenesGroup;
        Label sceneCountLabel;

        Button sceneBakeButton;
        Button sceneMergeButton;

        void SetupSceneUI()
        {

            scenesGroup = view.Q<GroupBox>("group-scenes");
            sceneCountLabel = scenesGroup.Q<Label>("count-scenes");

            sceneBakeButton = scenesGroup.Q<Button>("button-bake");
            sceneMergeButton = scenesGroup.Q<Button>("button-merge");

            sceneBakeButton.clicked += () => ContextualActions.BakeLightmaps(actionableScenes);
            sceneMergeButton.clicked += () => ContextualActions.MergeScenes(actionableScenes);
            scenesGroup.Q<Button>("button-remove").clicked += () => ContextualActions.Remove(selectedScenes);

        }

        void UpdateSceneUI()
        {

            scenesGroup.SetVisible(selectedScenes.Count > 0);
            sceneCountLabel.text = selectedScenes.Count().ToString();

            var sceneCount = actionableScenes.Count();
            sceneBakeButton.SetEnabled(sceneCount > 1);
            sceneMergeButton.SetEnabled(sceneCount > 1);

        }

        #endregion

        #endregion
        #region Selection

        void SetupSelection()
        {
            rootVisualElement.UnregisterCallback<PointerDownEvent>(PointerDown);
            rootVisualElement.RegisterCallback<PointerDownEvent>(PointerDown);
        }

        void PointerDown(PointerDownEvent e)
        {
            if (((VisualElement)e.target).GetAncestor<ObjectField>() is null)
                Clear();
        }

        public IEnumerable<CollectionScenePair> scenes => selectedScenes;
        public IEnumerable<SceneCollection> collections => selectedCollections;

        public void Add(SceneItem item) => SetSelection(item, true);
        public void Add(CollectionItem item) => SetSelection(item, true);

        public void Remove(SceneItem item) => SetSelection(item, false);
        public void Remove(CollectionItem item) => SetSelection(item, false);

        public void Remove(IEnumerable<ISceneCollection> collections)
        {
            selectedCollections.RemoveAll(c => c == collections.Contains(c));
            Save();
            UpdateUI();
        }

        public void Remove(ISceneCollection collection)
        {
            if (collection is SceneCollection c)
                selectedCollections.Remove(c);
            Save();
            UpdateUI();
        }

        public void Remove(ISceneCollection collection, int sceneIndex)
        {
            if (collection is SceneCollection c)
                selectedScenes.RemoveAll(s => s.collection == c && s.sceneIndex == sceneIndex);
            Save();
            UpdateUI();
        }

        public void Remove(IEnumerable<CollectionScenePair> items)
        {
            selectedScenes.RemoveAll(s => items.Contains(s));
            Save();
            UpdateUI();
        }

        public void SetSelection(SceneItem item, bool value)
        {
            if (IsSelected(item) != value)
                ToggleSelection(item);
        }

        public void SetSelection(CollectionItem item, bool value)
        {
            if (IsSelected(item) != value)
                ToggleSelection(item);
        }

        public void ToggleSelection(SceneItem item)
        {

            if (item.collection is not SceneCollection c)
                return;

            var existingItem = selectedScenes.FirstOrDefault(i => i.collection == c && i.sceneIndex == item.index);
            if (!selectedScenes.Remove(existingItem))
                selectedScenes.Add(new() { collection = c, sceneIndex = item.index });

            Save();
            UpdateUI();

        }

        public void ToggleSelection(CollectionItem item)
        {

            if (item.collection is not SceneCollection c)
                return;

            if (!selectedCollections.Remove(c))
                selectedCollections.Add(c);

            Save();
            UpdateUI();

        }

        public bool IsSelected(SceneItem item) =>
            selectedScenes.Any(i => i.collection == item.collection as SceneCollection && i.sceneIndex == item.index);

        public bool IsSelected(CollectionItem item) =>
            selectedCollections.Contains(item.collection as SceneCollection);

        public void Clear()
        {

            if (!selectedCollections.Any() && !selectedScenes.Any())
                return;

            selectedCollections.Clear();
            selectedScenes.Clear();
            Save();

            DependencyInjectionUtility.GetService<ISceneManagerWindow>().Reload();
            UpdateUI();

        }

        #endregion

    }

}
