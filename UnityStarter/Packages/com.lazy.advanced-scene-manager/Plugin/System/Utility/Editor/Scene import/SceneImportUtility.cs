#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;
using static AdvancedSceneManager.Editor.Utility.SceneImportUtility.StringExtensions;

namespace AdvancedSceneManager.Editor.Utility
{

    /// <summary>Contains utility functions for importing / un-importing scenes.</summary>
    public partial class SceneImportUtility : AssetPostprocessor
    {

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) =>
            EditorApplication.delayCall += () =>
            {
                MoveAssets(movedAssets, movedFromAssetPaths);
                ImportAssets(importedAssets);
                DeleteAssets(deletedAssets);
                Notify();
            };

        #region Freeze

        static readonly List<Scene> frozenScenes = new();

        /// <summary>Prevents a scene from being unimported when its associated scene asset is removed.</summary>
        internal static void Freeze(Scene scene) =>
            frozenScenes.Add(scene);

        /// <summary>Prevents a scene from being unimported when its associated scene asset is removed.</summary>
        internal static void UnFreeze(Scene scene) =>
            frozenScenes.Remove(scene);

        #endregion
        #region Import

        static void ImportAssets(string[] importedAssets)
        {

            var scenesToImport = importedAssets.Where(IsValidSceneToImport).ToArray();

            if (scenesToImport.Any() && SceneManager.settings.project.sceneImportOption is SceneImportOption.SceneCreated)
                _ = Import(scenesToImport);

        }

        public static IEnumerable<Scene> Import(IEnumerable<string> sceneAssetPaths, bool notify = true) =>
            Import(sceneAssetPaths, SceneManager.settings.project.assetPath, notify);

        public static IEnumerable<Scene> Import(IEnumerable<string> sceneAssetPaths, string importFolder, bool notify = true)
        {

            var list = new List<Scene>();

            var paths = sceneAssetPaths.Where(path => !IsImported(path)).ToArray();

            AssetDatabase.StartAssetEditing();

            var i = 0;
            foreach (var path in paths)
            {

                try
                {
                    list.Add(Import(path, importFolder, notify: false, track: false, skipImportedCheck: false));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                i += 1;

            }

            AssetDatabase.StopAssetEditing();
            Assets.Add(list, importFolder);
            LogUtility.LogImported(list);

            if (notify && list.Any())
                Notify();

            return list;

        }

        public static Scene Import(string sceneAssetPath, bool notify = true, bool track = true) =>
            Import(sceneAssetPath, SceneManager.settings.project.assetPath, notify, track);

        public static Scene Import(string sceneAssetPath, string importFolder, bool notify = true, bool track = true, bool skipImportedCheck = false)
        {

            if (!skipImportedCheck && IsImported(sceneAssetPath))
                throw new InvalidOperationException("Cannot import a scene that is already imported!");

            var scene = ScriptableObject.CreateInstance<Scene>();
            ((ScriptableObject)scene).name = Path.GetFileNameWithoutExtension(sceneAssetPath);
            scene.m_id = ASMModel.GenerateID();

            Assets.SetSceneAssetPath(scene, sceneAssetPath, false);
            scene.CheckIfSpecialScene();

            if (track)
                Assets.Add(scene, importFolder);

            if (notify)
            {
                Notify();
                LogUtility.LogImported(sceneAssetPath);
            }

            return scene;

        }

        #endregion
        #region Unimport

        static void DeleteAssets(string[] deletedAssets) =>
            Unimport(GetImportedScenes(deletedAssets));

        public static void Unimport(IEnumerable<string> scenes, bool notify = true) =>
            Unimport(GetImportedScenes(scenes).NonNull(), notify);

        public static void Unimport(IEnumerable<Scene> scenes, bool notify = true)
        {

            var list = scenes.ToArray();
            if (list.Any())
            {
                var progressID = Progress.Start("Unimporting scenes...");

                try
                {

                    AssetDatabase.StartAssetEditing();

                    var i = 0;
                    foreach (var scene in list)
                    {
                        Progress.Report(progressID, (float)i / list.Length, scene.path);
                        Unimport(scene, false);
                        i += 1;
                    }

                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    Progress.Remove(progressID);
                    AssetDatabase.StopAssetEditing();
                }

                Assets.Save();
                LogUtility.LogUnimported(list);

                if (notify)
                    Notify();

            }

        }

        public static void Unimport(Scene scene, bool notify = true)
        {

            if (!scene)
                throw new ArgumentNullException(nameof(scene));

            if (frozenScenes.Contains(scene))
                return;

            var path = scene.path;
            Assets.Remove(scene, notify);

            if (notify)
            {
                Notify();
                LogUtility.LogUnimported(path);
            }

        }

        #endregion
        #region Move

        static void MoveAssets(string[] movedAssets, string[] movedFromAssetPaths)
        {
            for (int i = 0; i < movedFromAssetPaths.Length; i++)
                if (GetImportedScene(movedFromAssetPaths[i], out var scene))
                {
                    Assets.SetSceneAssetPath(scene, movedAssets[i]);
                    scene.Rename(Path.GetFileNameWithoutExtension(movedAssets[i]));
                }
        }

        #endregion
        #region Get imported scenes

        public static Scene GetImportedSceneByItsOwnPath(string path)
        {
            if (path.EndsWith(".asset") && path.ToLower().Contains(Assets.assetPath.ToLower()))
                return AssetDatabase.LoadAssetAtPath<Scene>(path);
            else
                return null;
        }

        public static bool GetImportedScene(string sceneAssetPath, out Scene scene) =>
            scene = SceneManager.assets.scenes.FirstOrDefault(s => s && s.path.ToLower() == sceneAssetPath.ToLower());

        public static IEnumerable<Scene> GetImportedScenes(IEnumerable<string> sceneAssetPaths)
        {
            foreach (var path in sceneAssetPaths)
                if (GetImportedScene(path, out var scene))
                    yield return scene;
        }

        #endregion

        internal static void Notify()
        {

            EditorApplication.delayCall -= DoNotify;
            EditorApplication.delayCall += DoNotify;

            void DoNotify() => scenesChanged?.Invoke();

        }

        /// <summary>Occurs when either <see cref="unimportedScenes"/>, <see cref="importedScenes"/>, or <see cref="invalidScenes"/> has changed.</summary>
        public static event Action scenesChanged;

        /// <summary>Gets the list of unimported scenes in the project, that are available for import.</summary>
        public static IEnumerable<string> unimportedScenes
        {
            get
            {

                if (!SceneManager.isInitialized)
                    return Enumerable.Empty<string>();

                var untracked = untrackedScenes.Select(s => s.path).ToArray();
                var invalid = scenesWithBadPath.Select(s => AssetDatabase.GetAssetPath(s.sceneAsset)).ToArray();

                return
                    AssetDatabase.FindAssets("t:SceneAsset").
                    Select(AssetDatabase.GUIDToAssetPath).
                    Where(IsValidSceneToImport).
                    Except(untracked).
                    Except(invalid).
                    Distinct();

            }
        }

        /// <summary>Gets the list of dynamic scenes in the current profile.</summary>
        public static IEnumerable<string> dynamicScenes =>
            SceneManager.profile
            ? SceneManager.profile.dynamicCollections.SelectMany(c => c.scenePaths)
            : Enumerable.Empty<string>();

        /// <summary>Gets the duplicate imported scenes.</summary>
        public static IEnumerable<IGrouping<string, Scene>> duplicateScenes =>
            SceneManager.assets.scenes.GroupBy(s => s.path).Where(g => g.Count() > 1);

        /// <summary>Gets the list of imported scenes in the project.</summary>
        public static IEnumerable<string> importedScenes =>
            SceneManager.assets.scenes.Where(s => s).Select(s => s.path);

        /// <summary>Gets the list of imported scenes that do not have a associated scene asset.</summary>
        public static IEnumerable<Scene> invalidScenes =>
            SceneManager.assets.scenes.Where(s => s && !s.hasSceneAsset && !s.isDefaultASMScene);

        /// <summary>Gets the list of imported scenes that do not have a associated scene asset.</summary>
        public static IEnumerable<Scene> scenesWithBadPath =>
            SceneManager.assets.scenes.Where(s => s && s.sceneAsset && !s.isDefaultASMScene && !IsPackageScene(s.path) && s.path.ToLower() != AssetDatabase.GetAssetPath(s.sceneAsset).ToLower());

        /// <summary>Gets the list of imported scenes that are blacklisted.</summary>
        public static IEnumerable<Scene> importedBlacklistedScenes =>
            SceneManager.assets.scenes.Where(s => s && IsBlacklisted(s.path));

        /// <summary>Gets the list of scenes that are imported, but are, for whatever reason, not tracked by AssetRef.</summary>
        public static IEnumerable<Scene> untrackedScenes
        {
            get
            {

                if (!SceneManager.isInitialized)
                    return Enumerable.Empty<Scene>();

                return AssetDatabase.FindAssets(Scene.AssetSearchString).
                    Select(AssetDatabase.GUIDToAssetPath).
                    Select(AssetDatabase.LoadAssetAtPath<Scene>).
                    NonNull().
                    Where(s => !Assets.scenes.Contains(s));

            }
        }
    }

}
#endif
