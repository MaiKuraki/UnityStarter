using System;
using System.Linq;
using AdvancedSceneManager.Utility;
using UnityEngine;
using AdvancedSceneManager.Core;
using System.Collections.Generic;


#if UNITY_EDITOR
using AdvancedSceneManager.Editor.Utility;
#endif

namespace AdvancedSceneManager.Models
{

    /// <summary>Provides utility methods for working with <see cref="SceneCollection"/>.</summary>
    public static class ASMModelExtensions
    {

        /// <summary>Saves the associated <see cref="ScriptableObject"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        static void Save<T>(this T collection) where T : ISceneCollection
        {
            if (collection is ASMModel model)
                model.Save();
            else if (collection is ScriptableObject so)
                so.Save();
            else
                foreach (var profile in SceneManager.assets.profiles)
                    profile.Save();
        }

        /// <summary>Finds the index of <paramref name="scene"/>.</summary>
        /// <remarks>Returns -1 if it does not exist.</remarks>
        public static int IndexOf<T>(this T collection, Scene scene) where T : ISceneCollection =>
            Array.IndexOf(collection.scenes.ToArray(), scene);

        #region ISceneCollection.IEditable

#if UNITY_EDITOR

        /// <summary>Adds an empty scene field to this <see cref="SceneCollection"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void AddEmptyScene<T>(this T collection) where T : ISceneCollection, ISceneCollection.IEditable
        {
            collection.sceneList.Add(null);
            collection.Save();
            collection.OnPropertyChanged(nameof(collection.scenes));
        }

        /// <summary>Adds a scene to this <see cref="SceneCollection"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void Add<T>(this T collection, params Scene[] scenes) where T : ISceneCollection, ISceneCollection.IEditable
        {

            var didAdd = false;
            foreach (var scene in scenes)
                if (!collection.Contains(scene))
                {
                    collection.sceneList.Add(scene);
                    didAdd = true;
                }

            if (didAdd)
            {
                collection.Save();
                BuildUtility.UpdateSceneList();
                collection.OnPropertyChanged(nameof(collection.scenes));
            }

        }

        /// <summary>Replaces a scene at the specified index.</summary>
        /// <remarks>Only available in editor.</remarks>
        /// <returns><see langword="true"/> if replace was successful.</returns>
        public static bool Replace<T>(this T collection, int index, Scene scene) where T : ISceneCollection, ISceneCollection.IEditable
        {

            if (index > collection.count - 1)
                return false;

            collection.sceneList[index] = scene;
            collection.Save();
            BuildUtility.UpdateSceneList();

            return true;

        }

        public static void Insert<T>(this T collection, int index, Scene scene) where T : ISceneCollection, ISceneCollection.IEditable
        {

            index = Math.Clamp(index, 0, collection.count);
            collection.sceneList.Insert(index, scene);
            collection.Save();
            BuildUtility.UpdateSceneList();
            collection.OnPropertyChanged(nameof(collection.scenes));

        }

        /// <summary>Removes a scene from this <see cref="SceneCollection"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void Remove<T>(this T collection, Scene scene) where T : ISceneCollection, ISceneCollection.IEditable
        {
            if (collection.sceneList.Remove(scene))
            {
                collection.Save();
                BuildUtility.UpdateSceneList();
                collection.OnPropertyChanged(nameof(collection.scenes));
            }
        }

        /// <summary>Removes a scene at the specified index from this <see cref="SceneCollection"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void RemoveAt<T>(this T collection, int index) where T : ISceneCollection, ISceneCollection.IEditable
        {
            collection.sceneList.RemoveAt(index);
            collection.Save();
            BuildUtility.UpdateSceneList();
            collection.OnPropertyChanged(nameof(collection.scenes));
        }

        /// <summary>Moves a scene field to a new index.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void Move<T>(this T collection, int oldIndex, int newIndex) where T : ISceneCollection, ISceneCollection.IEditable
        {

            var item = collection.sceneList[oldIndex];
            collection.sceneList.RemoveAt(oldIndex);

            collection.sceneList.Insert(newIndex, item);
            collection.Save();
            collection.OnPropertyChanged(nameof(collection.scenes));

        }

#endif

        #endregion
        #region SceneCollection

        /// <summary>Opens the <paramref name="collections"/> as additive.</summary>
        public static SceneOperation OpenAdditive(this IEnumerable<SceneCollection> collections) =>
            SceneManager.runtime.OpenAdditive(collections);

        /// <summary>Opens the <paramref name="collections"/> as additive.</summary>
        /// <remarks>If <paramref name="activeCollection"/> is part of <paramref name="collections"/>, then it will only be opened once, not as additive.</remarks>
        public static SceneOperation OpenAdditive(this IEnumerable<SceneCollection> collections, SceneCollection activeCollection) =>
            SceneManager.runtime.OpenAdditive(collections, activeCollection);

        /// <summary>Opens the <paramref name="collections"/> as additive.</summary>
        /// <remarks>If <paramref name="activeCollection"/> is part of <paramref name="collections"/>, then it will only be opened once, not as additive.</remarks>
        public static SceneOperation OpenAdditive(this IEnumerable<SceneCollection> collections, SceneCollection activeCollection, Scene loadingScene) =>
            SceneManager.runtime.OpenAdditive(collections, activeCollection, loadingScene);

        /// <summary>Opens this <paramref name="collection"/> and then opens <paramref name="extraAdditiveCollections"/> as additive.</summary>
        public static SceneOperation OpenWithAdditive(this SceneCollection collection, params SceneCollection[] extraAdditiveCollections) =>
            SceneManager.runtime.OpenAdditive(extraAdditiveCollections, collection);

        #endregion
        #region Scene

        /// <summary>Opens the <paramref name="scenes"/>.</summary>
        public static SceneOperation OpenAll(this IEnumerable<Scene> scenes) =>
            SceneManager.runtime.Open(scenes);

        /// <summary>Opens the <paramref name="scenes"/>.</summary>
        /// <param name="scenes">The scenes to open.</param>
        /// <param name="loadingScene">Cover this operation with <paramref name="loadingScene"/>.</param>
        public static SceneOperation OpenAll(this IEnumerable<Scene> scenes, Scene loadingScene) =>
            SceneManager.runtime.Open(scenes).With(loadingScene: loadingScene);

        /// <summary>Closes the <paramref name="scenes"/>.</summary>
        /// <param name="scenes">The scenes to open.</param>
        public static SceneOperation CloseAll(this IEnumerable<Scene> scenes) =>
            SceneManager.runtime.Close(scenes);

        /// <summary>Closes the <paramref name="scenes"/>.</summary>
        /// <param name="scenes">The scenes to open.</param>
        /// <param name="loadingScene">Cover this operation with <paramref name="loadingScene"/>.</param>
        public static SceneOperation CloseAll(this IEnumerable<Scene> scenes, Scene loadingScene) =>
            SceneManager.runtime.Close(scenes).With(loadingScene: loadingScene);

        #endregion


    }

}
