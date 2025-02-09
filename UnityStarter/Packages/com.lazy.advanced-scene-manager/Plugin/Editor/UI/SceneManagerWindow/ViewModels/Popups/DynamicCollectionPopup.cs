using System;
using System.IO;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class DynamicCollectionPopup : ViewModel, IPopup
    {

        string savedCollectionID
        {
            get => Get(string.Empty);
            set => Set(value);
        }

        DynamicCollection collection;

        public override void PassParameter(object parameter)
        {
            if (parameter is DynamicCollection collection)
                this.collection = collection;
            else
                throw new ArgumentException("Bad parameter. Must be DynamicSceneCollection.");
        }

        public override void OnReopen() =>
            collection = Profile.current.dynamicCollections.FirstOrDefault(c => c.id == savedCollectionID);

        public override void OnAdded()
        {

            if (collection == null)
            {
                ClosePopup();
                return;
            }

            savedCollectionID = collection.id;

            view.Q<TextField>("text-title").BindTwoWay(collection, nameof(collection.title));
            view.Q<TextField>("text-path").BindTwoWay(collection, nameof(collection.path));

            view.Q<TextField>("text-path").RegisterCallback<FocusOutEvent>(e => collection.ReloadPaths());

            view.Q<Button>("button-pick").clicked += () =>
            {

                var path = collection.path;
                if (string.IsNullOrEmpty(path))
                    path = Application.dataPath;

                var folder = EditorUtility.OpenFolderPanel("Pick folder...", path, "");
                if (Directory.Exists(folder))
                {
                    collection.path = AssetDatabaseUtility.MakeRelative(folder);
                    collection.ReloadPaths();
                }

            };

            view.Q<ScrollView>().PersistScrollPosition();

        }

        public override void OnRemoved()
        {
            savedCollectionID = null;
            collection.ReloadPaths();
            view.Q<ScrollView>().ClearScrollPosition();
        }

    }

}
