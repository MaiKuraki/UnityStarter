using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views
{

    class UndoView : ViewModel, IView
    {

        const double undoTimeout = 10;
        readonly Dictionary<ISceneCollection, (ProgressBar progressBar, float timeAdded)> undoTimeouts = new();

        public VisualTreeAsset undoTemplate => viewLocator.items.undo;

        ListView list;
        public override void OnAdded()
        {

            Profile.onProfileChanged += Reload;
            EditorApplication.update += Update;

            list = view.Q<ListView>();
            if (Profile.current)
                Reload();

        }

        public override void OnRemoved()
        {

            Profile.onProfileChanged -= Reload;
            EditorApplication.update -= Update;
        }

        public void Reload()
        {

            if (!isAdded)
                return;

            if (Profile.current)
            {
                list.bindItem = BindCollection;
                list.makeItem = () => Instantiate(undoTemplate);
            }
            else
                list.Unbind();

            list.itemsSource = Profile.current ? Profile.current.removedCollections.ToArray() : Array.Empty<SceneCollection>();
            list.Rebuild();

        }

        void BindCollection(VisualElement element, int index)
        {

            if (Profile.current.removedCollections.ElementAtOrDefault(index) is not ISceneCollection collection)
                return;

            if (collection is SceneCollection c)
                element.Bind(new(c));
            element.userData = collection;

            element.Q<Label>("label-name").text = collection.title + " has been removed.";

            var buttonUndo = element.Q<Button>("button-undo");
            var buttonDelete = element.Q<Button>("button-delete");

            buttonUndo.clickable = null;
            buttonDelete.clickable = null;

            var progressBar = element.Q<ProgressBar>();
            progressBar.lowValue = 0;
            progressBar.highValue = 1;

            if (!undoTimeouts.ContainsKey(collection))
                undoTimeouts.Add(collection, (progressBar, (float)EditorApplication.timeSinceStartup));
            else
                undoTimeouts[collection] = (progressBar, undoTimeouts[collection].timeAdded);

            buttonUndo.clicked += () =>
            {

                undoTimeouts.Remove(collection);
                Profile.current.Restore(collection);
                Reload();

            };

            buttonDelete.clicked += () =>
                Remove(collection);

        }

        void Remove(ISceneCollection collection)
        {
            undoTimeouts.Remove(collection);
            if (Profile.current)
                Profile.current.Delete(collection);
            Reload();
        }

        void Update()
        {

            foreach (var (collection, (progressBar, timeAdded)) in undoTimeouts.ToArray())
            {

                var value = ((EditorApplication.timeSinceStartup - timeAdded) / undoTimeout);
                if (value >= 1)
                    Remove(collection);
                else
                    progressBar.value = (float)value;

            }

        }

    }

}
