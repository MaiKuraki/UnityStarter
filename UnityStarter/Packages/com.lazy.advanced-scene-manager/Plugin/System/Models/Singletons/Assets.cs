using Object = UnityEngine.Object;
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using AdvancedSceneManager.Utility;
using AdvancedSceneManager.Models.Utility;
using AdvancedSceneManager.Models.Helpers;

#if UNITY_EDITOR
using AdvancedSceneManager.Editor.Utility;
using UnityEditor;
#endif

namespace AdvancedSceneManager.Models.Internal
{

    /// <summary>Manages all ASM assets.</summary>
    [InitializeInEditor]
    static class Assets
    {

        #region Properties

        #region Internal

        static bool isInitialized => ASMSettings.isInitialized;

        static List<Profile> m_profiles
        {
            get => ASMSettings.instance.m_profiles;
        }

        static List<Scene> m_scenes
        {
            get => ASMSettings.instance.m_scenes;
        }

        static List<SceneCollection> m_collections
        {
            get => ASMSettings.instance.m_collections;
        }

        static List<SceneCollectionTemplate> m_collectionTemplates
        {
            get => ASMSettings.instance.m_collectionTemplates;
        }

        static ASMSceneHelper m_sceneHelper
        {
            get => ASMSettings.instance.m_sceneHelper;
            set => ASMSettings.instance.m_sceneHelper = value;
        }

        static DefaultScenes m_defaultScenes
        {
            get => ASMSettings.instance.m_defaultScenes;
            set => ASMSettings.instance.m_defaultScenes = value;
        }

        static string GetFallbackScenePath()
        {
            if (Profile.current && Profile.current.startupScene)
                return Profile.current.startupScene.path;
            else
#if UNITY_EDITOR
                return $"{SceneManager.package.folder}/Plugin/Assets/{FallbackSceneUtility.Name}.unity";
#else
                return $"Packages/com.lazy.advanced-scene-manager/Plugin/Assets/{FallbackSceneUtility.Name}.unity";
#endif

        }

        #endregion

        /// <summary>Enumerates all imported profiles.</summary>
        public static IEnumerable<Profile> profiles => isInitialized ? m_profiles.NonNull() : Enumerable.Empty<Profile>();

        /// <summary>Enumerates all imported collections.</summary>
        public static IEnumerable<SceneCollection> collections => isInitialized ? m_collections.NonNull() : Enumerable.Empty<SceneCollection>();

        /// <summary>Enumerates all imported collection templates.</summary>
        public static IEnumerable<SceneCollectionTemplate> collectionTemplates => isInitialized ? m_collectionTemplates.NonNull() : Enumerable.Empty<SceneCollectionTemplate>();

        /// <summary>Enumerates all imported scenes.</summary>
        public static IEnumerable<Scene> scenes => isInitialized ? m_scenes.NonNull() : Enumerable.Empty<Scene>();

        /// <summary>Gets scene helper singleton.</summary>
        public static ASMSceneHelper sceneHelper => isInitialized ? m_sceneHelper : null;

        /// <summary>Gets default scenes singleton.</summary>
        public static DefaultScenes defaultScenes => isInitialized ? m_defaultScenes : null;

        /// <summary>Enumerates all imported assets.</summary>
        public static IEnumerable<Object> allAssets =>
            profiles.OfType<Object>().Concat(scenes).Concat(collections).Concat(collectionTemplates).Concat(new[] { sceneHelper }).NonNull();

        /// <summary>Gets the import path.</summary>
        /// <remarks>Can be changed using <see cref="ProjectSettings.assetPath"/></remarks>
        public static string assetPath => SceneManager.settings.project ? SceneManager.settings.project.assetPath : string.Empty;

        /// <summary>Gets the path to the fallback scene.</summary>
        public static string fallbackScenePath => GetFallbackScenePath();

        #endregion
        #region Scene paths

#if UNITY_EDITOR

        /// <summary>Sets scene path and asset.</summary>
        /// <remarks>Only available in editor.</remarks>
        internal static void SetSceneAssetPath(Scene scene, string path, bool save = true)
        {

            if (!scene)
                return;

            var didChange = false;
            if (scene.path.ToLower() != path.ToLower())
            {
                scene.path = path;
                didChange = true;
            }

            if (!scene.isDefaultASMScene)
            {
                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                if (scene.sceneAsset != asset)
                {
                    scene.sceneAsset = asset;
                    didChange = true;
                }

                var guid = AssetDatabase.GUIDFromAssetPath(path).ToString();
                if (scene.m_sceneAssetGUID != guid)
                {
                    scene.m_sceneAssetGUID = guid;
                    didChange = true;
                }
            }

            if (didChange && save)
                scene.SaveNow();

        }

#endif
        #endregion
        #region Add / Remove

#if UNITY_EDITOR

        static void _AddInternal<T>(params T[] items) where T : ASMModel
        {

            var paths = items.NonNull().Where(EditorUtility.IsPersistent).Select(AssetDatabase.GetAssetPath).ToArray();

            var list = GetList<T>();
            if (items.Select(Add).Any())
            {
                LogUtility.LogImport<T>("Imported", paths);
            }

            bool Add(T item)
            {
                if (item && !list.Contains(item))
                {
                    list.Add(item);
                    AddToHooks(item);
                    return true;
                }
                return false;
            }

        }

        static void _RemoveInternal<T>(params T[] items) where T : ASMModel, new()
        {

            items = items.NonNull().Where(EditorUtility.IsPersistent).ToArray();
            var paths = items.Select(AssetDatabase.GetAssetPath).ToArray();
            var folders = items.Select(item => GetFolder<T>(item.id)).Where(AssetDatabase.IsValidFolder).ToArray();

            var list = GetList<T>();

            var failedPaths = new List<string>();
            AssetDatabase.DeleteAssets(folders, failedPaths);
            if (failedPaths.Any())
                Debug.LogError("The following assets could not be removed:\n" + string.Join("\n", failedPaths));

            if (items.Select(list.Remove).Any())
            {
                LogUtility.LogImport<T>("Unimported", paths);
            }

            foreach (var item in items)
                if (!EditorUtility.IsPersistent(item))
                    RemoveFromHooks(item);

        }

        static void RemoveInternal(bool save, params ASMModel[] items)
        {
            _RemoveInternal(items.OfType<Scene>().ToArray());
            _RemoveInternal(items.OfType<SceneCollection>().Except(items.OfType<SceneCollectionTemplate>()).ToArray());
            _RemoveInternal(items.OfType<SceneCollectionTemplate>().ToArray());
            _RemoveInternal(items.OfType<Profile>().ToArray());
            if (save)
                Save();
        }

        static List<T> GetList<T>()
        {
            if (typeof(T) == typeof(Profile)) return (List<T>)(object)m_profiles;
            else if (typeof(T) == typeof(SceneCollectionTemplate)) return (List<T>)(object)m_collectionTemplates;
            else if (typeof(T) == typeof(SceneCollection)) return (List<T>)(object)m_collections;
            else if (typeof(T) == typeof(Scene)) return (List<T>)(object)m_scenes;
            throw new InvalidOperationException("Could not find list of " + typeof(T).Name);
        }

#endif

        #endregion
        #region Import

#if UNITY_EDITOR

        public static void Remove(IEnumerable<ASMModel> list, bool save = true) =>
            RemoveInternal(save, list.ToArray());

        public static void Remove<T>(IEnumerable<T> list, bool save = true) where T : ASMModel =>
            list.ForEach(m => Remove(m, save));

        public static void Add<T>(IEnumerable<T> list, bool save = true) where T : ASMModel =>
            Add(list, SceneManager.settings.project.assetPath, save);

        public static void Add<T>(IEnumerable<T> list, string importFolder, bool save = true) where T : ASMModel
        {
            list.ForEach(m => Add(m, importFolder, false));
            if (save)
                Save();
        }

        public static void Remove<T>(T obj, bool save = true) where T : ASMModel =>
            RemoveInternal(save, obj);

        public static void Add<T>(T obj, bool save = true) where T : ASMModel, new() =>
            Add(obj, assetPath, save);

        public static void Add<T>(T obj, string importFolder, bool save = true) where T : ASMModel
        {

            if (!obj)
                return;

            var isASMScene = obj is Scene scene && scene && scene.isDefaultASMScene;

            if (!isASMScene && !IsImportedByPath(obj, importFolder))
                Import(obj, importFolder);

            _AddInternal(obj);
            if (save)
                Save();

        }

        public static bool IsImported<T>(T model) where T : ASMModel =>
            allAssets.Contains(model);

        public static bool Contains<T>(T model) where T : ASMModel =>
            IsImported(model);

        static bool IsImportedByPath(ASMModel obj, string importFolder)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            return path.StartsWith(importFolder);
        }

        static void Import(ASMModel obj, string importFolder)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                Create(obj, importFolder);
            else
                Move(obj, path, importFolder);
        }

        static void Create(ASMModel obj, string importFolder)
        {
            var path = GetPath(obj, importFolder);
            AssetDatabase.CreateAsset(obj, path);
        }

        static void Move(ASMModel obj, string path, string importFolder)
        {
            var importPath = GetPath(obj, importFolder);
            AssetDatabaseUtility.CreateFolder(Directory.GetParent(importPath).FullName);
            var error = AssetDatabase.MoveAsset(path, importPath);
            if (!string.IsNullOrEmpty(error))
                Debug.LogError(error);
        }

        public static string GetPath(ASMModel obj, string importFolder)
        {
            var path = $"{GetFolder(obj, importFolder)}/{obj.name}.asset";
            Directory.GetParent(path).Create();
            return path;
        }

        public static string GetFolder(ASMModel obj, string importFolder) =>
            $"{importFolder}/{obj.GetType().Name}/{obj.id}";

        public static string GetFolder<T>() where T : ASMModel, new() =>
            $"{assetPath}/{typeof(T).Name}";

        public static string GetFolder<T>(string id) where T : ASMModel, new() =>
            $"{GetFolder<T>()}/{id}";

        public static string GetPath<T>(string id, string name) where T : ASMModel, new() =>
            $"{GetFolder<T>(id)}/{name}.asset";

#endif

        #endregion
        #region Save

        public static void Save() =>
            ASMSettings.instance.Save();

#if UNITY_EDITOR

        public static bool Cleanup()
        {

            if (Application.isPlaying)
                return false;

            CleanupFolder(GetFolder<Profile>());
            CleanupFolder(GetFolder<Scene>());
            CleanupFolder(GetFolder<SceneCollection>());
            CleanupFolder(GetFolder<SceneCollectionTemplate>());

            var needsSave = false;
            if (EnsureAssetsAdded<Profile>() ||
                EnsureAssetsAdded<SceneCollection>() ||
                EnsureAssetsAdded<SceneCollectionTemplate>())
                needsSave = true;

            if (CleanupNulls())
                needsSave = true;

            return needsSave;

        }

        public static void CleanupFolder(string folder)
        {

            var emptySceneFolders = AssetDatabase.GetSubFolders(folder);

            foreach (var subfolder in emptySceneFolders)
            {

                var path = Application.dataPath + subfolder.Replace("Assets", "");

                if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                    AssetDatabase.DeleteAsset(subfolder);

            }

        }

        internal static bool EnsureAssetsAdded<T>() where T : ASMModel
        {

            var list = GetList<T>();

            var existingPaths = list.Select(AssetDatabase.GetAssetPath).ToArray();
            var paths = AssetDatabaseUtility.FindAssetPaths<T>().Where(path => !existingPaths.Contains(path));

            var assets = paths.Select(AssetDatabase.LoadAssetAtPath<T>).Where(m => m.hasID).ToArray();

            foreach (var asset in assets)
                list.Add(asset);

            return assets.Length > 0;

        }

        static bool CleanupNulls()
        {

            var itemsRemoved =
                m_scenes.RemoveAll(s => !s || !s.hasID) +
                m_collections.RemoveAll(c => !c || !c.hasID) +
                m_profiles.RemoveAll(p => !p || !p.hasID) +
                m_collectionTemplates.RemoveAll(c => !c || !c.hasID);

            return itemsRemoved > 0;

        }

#endif

        #endregion
        #region Hook

        static void AddToHooks(ASMModel model)
        {

#if UNITY_EDITOR

            SceneAsset sceneAsset = null;
            if (model is Scene scene && scene)
                sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);

            foreach (var hook in hooks)
            {
                hook.list.Add(model);
                if (sceneAsset)
                    hook.sceneAssets.Add(sceneAsset);
            }

#endif

        }

        static void RemoveFromHooks(ASMModel model)
        {
#if UNITY_EDITOR
            foreach (var hook in hooks)
                hook.list.Remove(model);
#endif

        }

#if UNITY_EDITOR

        static readonly List<ImportHook> hooks = new();

        /// <summary>Adds a hook for asset import. Released on <see cref="IDisposable.Dispose"/>.</summary>
        /// <param name="removeAssetsOnDispose">Automatically delete ASM assets that was collected by this hook on <see cref="IDisposable.Dispose"/>.</param>
        /// <param name="removeSceneAssetsOnDispose">Automatically delete <see cref="SceneAsset"/> that was collected by this hook on <see cref="IDisposable.Dispose"/>.</param>
        /// <remarks>Only available in editor.</remarks>
        public static ImportHook Hook(bool removeAssetsOnDispose = false, bool removeSceneAssetsOnDispose = false)
        {
            var hook = new ImportHook() { removeAssetsOnDispose = removeAssetsOnDispose, removeSceneAssetsOnDispose = removeSceneAssetsOnDispose };
            hooks.Add(hook);
            return hook;
        }

        /// <summary>Collects imported assets when added.</summary>
        /// <remarks>Only available in editor.</remarks>
        public class ImportHook : IDisposable
        {

            internal ImportHook()
            { }

            /// <summary>Specifies whatever collected ASM assets should be removed on <see cref="Dispose"/>.</summary>
            public bool removeAssetsOnDispose { get; set; }

            /// <summary>Specifies whatever collected <see cref="SceneAsset"/> should be removed on <see cref="Dispose"/>.</summary>
            public bool removeSceneAssetsOnDispose { get; set; }

            internal readonly List<SceneAsset> sceneAssets = new();
            internal readonly List<ASMModel> list = new();

            /// <summary>Enumerates the collected assets.</summary>
            public IEnumerable<ASMModel> importedModels => list.NonNull();

            /// <summary>Enumerates the collected <see cref="SceneAsset"/>.</summary>
            public IEnumerable<SceneAsset> addedScenes => sceneAssets.NonNull();

            /// <summary>Releases the hook.</summary>
            public void Dispose() => Release();

            /// <summary>Releases hook, and optionally removes assets, if specified by <see cref="removeAssetsOnDispose"/> and <see cref="removeSceneAssetsOnDispose"/>.</summary>
            public void Release()
            {
                if (removeAssetsOnDispose)
                    RemoveAll(removeSceneAssetsOnDispose);
                hooks.Remove(this);
            }

            /// <summary>Removes all collected ASM assets, and optionally all <see cref="SceneAsset"/>.</summary>
            /// <param name="removeSceneAssets">Specifies whatever all added <see cref="SceneAsset"/> should also be removed.</param>
            /// <remarks>See also: <see cref="RemoveAll(bool)"/> and <see cref="RemoveAllAndRelease(bool)"/>.</remarks>
            public void RemoveAll(bool removeSceneAssets = false)
            {

                Remove(importedModels);

                if (removeSceneAssets)
                {

                    var paths = addedScenes.NonNull().Where(EditorUtility.IsPersistent).Select(AssetDatabase.GetAssetPath).ToArray();

                    var failedPaths = new List<string>();
                    AssetDatabase.DeleteAssets(paths, failedPaths);

                    if (failedPaths.Any())
                        Debug.LogError("Could not remove the following assets:\n" + string.Join("\n", failedPaths));

                }

            }

            /// <summary>Removes all collected ASM assets, and optionally all <see cref="SceneAsset"/>, then releases.</summary>
            /// <param name="removeSceneAssets">Specifies whatever all added <see cref="SceneAsset"/> should also be removed.</param>
            public void RemoveAllAndRelease(bool removeSceneAssets = false)
            {
                RemoveAll(removeSceneAssets);
                Release();
            }

        }

#endif

        #endregion

    }

}
