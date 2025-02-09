using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Editor.UI.Layouts;
using AdvancedSceneManager.Editor.UI.Views;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Utility;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;

namespace AdvancedSceneManager.Editor.UI.Utility
{

    static class ContextualActions
    {

        public static void Remove(IEnumerable<ISceneCollection> collections)
        {

            foreach (var collection in collections)
                Profile.current.Remove(collection);

            EditorApplication.delayCall += DependencyInjectionUtility.GetService<UndoView>().Reload;
            DependencyInjectionUtility.GetService<SelectionView>().Remove(collections);

        }

        public static void Remove(ISceneCollection collection)
        {

            Profile.current.Remove(collection);

            EditorApplication.delayCall += DependencyInjectionUtility.GetService<UndoView>().Reload;
            DependencyInjectionUtility.GetService<SelectionView>().Remove(collection);

        }

        public static void Remove(IEnumerable<CollectionScenePair> items)
        {

            var groupedItems = items.GroupBy(i => i.collection).Select(g => (collection: g.Key, scenes: g.Select(i => i.sceneIndex).ToArray()));

            foreach (var collection in groupedItems)
                foreach (var index in collection.scenes.OrderByDescending(s => s))
                    if (collection.collection is ISceneCollection.IEditable c)
                        Remove(c, index);

        }

        public static void Remove(ISceneCollection.IEditable collection, int sceneIndex)
        {
            collection.RemoveAt(sceneIndex);
            DependencyInjectionUtility.GetService<SelectionView>().Remove(collection, sceneIndex);
        }

        public static void CreateTemplate(SceneCollection collection)
        {
            SceneCollectionTemplate.CreateTemplate(collection);
            DependencyInjectionUtility.GetService<PopupView>().Open<ExtraCollectionPopup>();
        }

        public static void ViewInProjectView(Object obj)
        {
            EditorGUIUtility.PingObject(obj);
        }

        public static void Open(IEnumerable<Scene> scenes, bool additive)
        {
            if (!additive)
                SceneManager.runtime.Open(scenes).CloseAll();
            else
                SceneManager.runtime.Open(scenes);
        }

        public static void MergeScenes(IEnumerable<Scene> scenes)
        {

            scenes = scenes.Distinct();
            var targetScene = scenes.First().path;
            var mergeScenes = scenes.Skip(1).Select(s => s.path).ToArray();

            SceneUtility.MergeScenes(targetScene, mergeScenes);

        }

        public static void BakeLightmaps(IEnumerable<Scene> scenes) =>
            Lightmapping.BakeMultipleScenes(scenes.Distinct().Select(s => s.path).ToArray());

    }

}
