using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AdvancedSceneManager.Utility;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AdvancedSceneManager.Models
{

    [Serializable]
    public class DefaultASMScenesCollection : ISceneCollection
    {

        const string defaultCollectionDescription =
            "ASM contains some default scenes that you may use or take inspiration from. " +
            "The scenes are provided as a UPM sample, you may use the button below, or use the package manager, to import it.";

        [SerializeField] internal string m_id = GuidReferenceUtility.GenerateID();

        public bool isImported;

        public Scene this[int index] => scenes.ElementAtOrDefault(index);

        public IEnumerable<Scene> scenes => SceneManager.assets.defaults.Enumerate();
        public IEnumerable<string> scenePaths => scenes.Select(s => s.path);
        public string title => "ASM Defaults";
        public string description => defaultCollectionDescription;
        public int count => scenes.Count();
        public string id => m_id;

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new(propertyName));

        public IEnumerator<Scene> GetEnumerator() =>
            scenes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();

#if UNITY_EDITOR

        /// <summary>Imports the sample containing the default scenes.</summary>
        public static void ImportScenes()
        {

            var folder = SceneManager.package.folder + "/Samples~/Default ASM Scenes";

            var destinationFolder = $"Assets/Samples/Advanced Scene Manager/{SceneManager.package.version}/Default ASM scenes";

            List<string> failed = new();
            if (!AssetDatabase.DeleteAssets(SceneManager.assets.defaults.Enumerate().Select(s => s.path).ToArray(), failed))
                Debug.LogError("Could not delete the following scenes:\n\n" + string.Join("\n", failed));

            AssetDatabase.DeleteAsset(destinationFolder);
            AssetDatabase.Refresh();

            EditorApplication.delayCall += () =>
            {
                FileUtil.CopyFileOrDirectory(folder, destinationFolder);
                AssetDatabase.Refresh();
            };

        }

        /// <summary>Removes the default scenes.</summary>
        public static void Unimport()
        {

            List<string> failed = new();
            if (!AssetDatabase.DeleteAssets(SceneManager.assets.defaults.Enumerate().Select(s => s.path).ToArray(), failed))
                Debug.LogError("Could not delete the following scenes:\n\n" + string.Join("\n", failed));

            if (Profile.current)
                Profile.current.RemoveDefaultASMScenes();

        }

#endif

    }

#if UNITY_EDITOR
    class DefaultASMScenesPostProcessor : AssetPostprocessor
    {

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {

            var paths = importedAssets.Where(path => path.StartsWith("Assets/Samples/Advanced Scene Manager") && path.EndsWith(".unity"));
            if (!paths.Any())
                return;

            var scenes = SceneUtility.Import(paths.ToArray());

            foreach (var scene in scenes)
            {
                scene.m_isDefaultASMScene = true;
                scene.Save();
            }

            if (Profile.current)
                Profile.current.AddDefaultASMScenes();


        }

    }
#endif

}
