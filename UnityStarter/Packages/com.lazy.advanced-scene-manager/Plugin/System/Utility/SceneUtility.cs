#pragma warning disable IDE0051 // Remove unused private members

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using scene = UnityEngine.SceneManagement.Scene;
using sceneManager = UnityEngine.SceneManagement.SceneManager;
using AdvancedSceneManager.Models;
using Scene = AdvancedSceneManager.Models.Scene;
using Object = UnityEngine.Object;
using AdvancedSceneManager.Core;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using AdvancedSceneManager.Editor.Utility;
#endif

namespace AdvancedSceneManager.Utility
{

    /// <summary>An utility class to perform actions on scenes.</summary>
    public static class SceneUtility
    {

        /// <summary>Get all open unity scenes.</summary>
        public static IEnumerable<scene> GetAllOpenUnityScenes()
        {
            for (int i = 0; i < sceneManager.sceneCount; i++)
                yield return sceneManager.GetSceneAt(i);
        }

        /// <summary>Gets if current, and only, scene is the startup scene.</summary>
        public static bool isStartupScene =>
            SceneUtility.GetAllOpenUnityScenes().All(s => FallbackSceneUtility.IsFallbackScene(s) || FallbackSceneUtility.GetStartupScene() == s.path);

        /// <summary>Gets the dontDestroyOnLoad scene. Returns null if not open.</summary>
        public static scene dontDestroyOnLoadScene => SceneManager.runtime.dontDestroyOnLoadScene;

        /// <summary>Gets if there are any scenes open that are not dynamically created, and not yet saved to disk.</summary>
        public static bool hasAnyScenes => sceneManager.sceneCount > 0 && !(unitySceneCount == 1 && FallbackSceneUtility.IsFallbackScene(sceneManager.GetSceneAt(0)));

        /// <inheritdoc cref="sceneManager.sceneCount"/>
        public static int unitySceneCount => sceneManager.sceneCount;

        /// <summary>Gets if the scene is included in build.</summary>
        public static bool IsIncluded(Scene scene) =>
            scene && !string.IsNullOrEmpty(scene.path) && UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(scene.path) != -1;

        #region Move / create game objects

        /// <inheritdoc cref="sceneManager.MoveGameObjectToScene(GameObject, scene)"/>
        public static void Move(this GameObject obj, Scene scene) =>
            Move(obj, scene ? scene.internalScene ?? default : default);

        /// <inheritdoc cref="sceneManager.MoveGameObjectToScene(GameObject, scene)"/>
        public static void Move(this GameObject obj, scene scene)
        {

            if (!scene.IsValid() || !obj)
                return;

            sceneManager.MoveGameObjectToScene(obj, scene);

        }

        /// <summary>Moves <paramref name="obj"/> to this scene.</summary>
        public static GameObject MoveHere(this MonoBehaviour mono, GameObject obj)
        {

            if (!obj)
                throw new ArgumentNullException(nameof(mono));

            if (!mono || !mono.gameObject)
                throw new ArgumentNullException(nameof(mono));

            obj.Move(mono.gameObject.scene);

            return obj;

        }

        /// <summary>Creates a game object in this scene.</summary>
        public static GameObject CreateHere(this MonoBehaviour mono)
        {

            if (!mono || !mono.gameObject)
                throw new ArgumentNullException(nameof(mono));

            return mono.MoveHere(new GameObject());

        }

        /// <summary>Creates a game object in this scene.</summary>
        public static GameObject CreateHere(this MonoBehaviour mono, string name)
        {

            if (!mono || !mono.gameObject)
                throw new ArgumentNullException(nameof(mono));

            return mono.MoveHere(new GameObject(name));

        }

        /// <summary>Creates a game object in this scene.</summary>
        public static GameObject CreateHere(this MonoBehaviour mono, string name, params Type[] components)
        {

            if (!mono || !mono.gameObject)
                throw new ArgumentNullException(nameof(mono));

            return mono.MoveHere(new GameObject(name, components));

        }

        /// <summary>Creates a game object in this scene. Adds and returns component <typeparamref name="TComponent"/>.</summary>
        public static TComponent CreateHere<TComponent>(this MonoBehaviour mono, string gameObjectName) where TComponent : Component
        {

            if (!mono || !mono.gameObject)
                throw new ArgumentNullException(nameof(mono));

            var obj = mono.MoveHere(new GameObject(gameObjectName));
            return obj.AddComponent<TComponent>();

        }

        #endregion
        #region Create

        /// <summary>Creates a scene at runtime, that is not saved to disk.</summary>
        /// <remarks>Returns <see langword="null"/> if scene could not be created.</remarks>
        public static Scene CreateDynamic(string name, UnityEngine.SceneManagement.LocalPhysicsMode localPhysicsMode = UnityEngine.SceneManagement.LocalPhysicsMode.None)
        {

            if (!Application.isPlaying)
                return null;

            if (string.IsNullOrWhiteSpace(name))
                return null;

            var uScene = sceneManager.CreateScene(name, new(localPhysicsMode));
            if (!uScene.IsValid())
                return null;

            var scene = ScriptableObject.CreateInstance<Scene>();
            ((Object)scene).name = name;
            SceneManager.runtime.Track(scene, uScene);
            return scene;

        }

#if UNITY_EDITOR

        /// <summary>Creates and imports a scene.</summary>
        /// <remarks>Only usable in editor</remarks>
        /// <param name="path">The path that the scene should be saved to.</param>
        public static Scene CreateAndImport(string path) =>
            CreateAndImport(new[] { path }).FirstOrDefault();

        /// <inheritdoc cref="CreateAndImport(string)"/>
        public static IEnumerable<Scene> CreateAndImport(params string[] paths) =>
            CreateAndImport(paths?.AsEnumerable()).ToArray();

        /// <inheritdoc cref="CreateAndImport(string)"/>
        public static IEnumerable<Scene> CreateAndImport(IEnumerable<string> paths) =>
            Create(paths).Import();

        /// <inheritdoc cref="Create(string)"/>
        public static IEnumerable<SceneAsset> Create(params string[] paths) =>
            Create(paths?.AsEnumerable()).ToArray();

        /// <inheritdoc cref="Create(string)"/>
        public static IEnumerable<SceneAsset> Create(IEnumerable<string> paths) =>
            paths?.Select(Create).ToArray() ?? Enumerable.Empty<SceneAsset>();

        /// <summary>Creates a scene at the specified path.</summary>
        /// <remarks>Only usable in editor</remarks>
        /// <param name="path">The path that the scene should be saved to.</param>
        public static SceneAsset Create(string path)
        {

            ValidatePath(path);

            path = NormalizePath(path);
            return CreateSceneFile(path);

        }

        static void ValidatePath(string path)
        {

            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path), "Name cannot be null or empty.");

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException(nameof(path), "Name cannot be whitespace.");

            //Windows / .net does not have an issue with paths over 260 chars anymore,
            //but unity still does, and it does not handle it gracefully, so let's have a check for that too
            //No clue how to make this cross-platform since we cannot even get the value on windows, so lets just hardcode it for now
            //This should be removed in the future when unity does handle it
            if (Path.GetFullPath(path).Length > 260)
                throw new PathTooLongException("Path cannot exceed 260 characters in length.");

        }

        static string NormalizePath(string path)
        {

            if (path is null)
                throw new ArgumentNullException(nameof(path));

            path = path.Replace('\\', '/');

            if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/"))
                path = "Assets/" + path;

            if (!path.EndsWith(".unity"))
                path += ".unity";

            return path;

        }

        /// <summary>Gets the template yaml for a scene file.</summary>
        public const string assetTemplate = "" +
            "%YAML 1.1\n" +
            "%TAG !u! tag:unity3d.com,2011:";

        static SceneAsset CreateSceneFile(string path)
        {

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (!sceneAsset)
            {

                CreateAssetFromTemplate(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

            }

            return
                sceneAsset
                ? sceneAsset
                : throw new Exception("Something went wrong when creating scene.");

        }

        internal static void CreateAssetFromTemplate(string path)
        {

            if (!File.Exists(path) || File.ReadAllText(path) != assetTemplate)
            {
                Directory.GetParent(path).Create();
                File.WriteAllText(path, assetTemplate);
            }

        }

#endif

        #endregion
        #region Find

        /// <inheritdoc cref="FindCollection(Scene)"/>
        public static bool FindCollection(this Scene scene, out SceneCollection collection) =>
            collection = FindCollection(scene);

        /// <summary>Attempts to find best match for collection.</summary>
        /// <remarks>Only checks current profile.</remarks>
        public static SceneCollection FindCollection(this Scene scene)
        {

            var collections = FindCollections(scene);
            if (collections.Count() == 0)
                return null;

            var collection = collections.FirstOrDefault(c => c.scenesToAutomaticallyOpen.Contains(scene));
            if (!collection) collection = collections.FirstOrDefault(c => c.scenes.Contains(scene));
            if (!collection) collection = collections.FirstOrDefault();

            return collection;

        }

        /// <summary>Finds which collections that this scene is a part of.</summary>
        public static IEnumerable<SceneCollection> FindCollections(this Scene scene, bool allProfiles = false) =>
            allProfiles
            ? FindCollections(scene, null)
            : FindCollections(scene, Profile.current);

        /// <summary>Finds which collections that this scene is a part of.</summary>
        public static IEnumerable<SceneCollection> FindCollections(this Scene scene, Profile profile) =>
            (profile ? profile.collections.ToArray() : SceneManager.assets.collections).
            Where(c => c && c.scenes != null && c.scenes.Contains(scene));

        /// <summary>Find open scenes by name or path.</summary>
        public static IEnumerable<Scene> FindOpen(string q) =>
            FindOpen(s => s.IsMatch(q));

        /// <summary>Find scenes by name or path.</summary>
        public static Scene Find(string q) =>
            Find(s => s && s.IsMatch(q)).FirstOrDefault();

        /// <summary>Find open scenes by predicate.</summary>
        public static IEnumerable<Scene> FindOpen(Func<Scene, bool> predicate) =>
            SceneManager.runtime.openScenes.Where(predicate);

        /// <summary>Find scenes by predicate.</summary>
        public static IEnumerable<Scene> Find(Func<Scene, bool> predicate) =>
            SceneManager.assets.scenes.Where(predicate);

        /// <inheritdoc cref="ASMScene(scene)"/>
        public static bool ASMScene(this Component component, out Scene scene) =>
            scene = ASMScene(component);

        /// <inheritdoc cref="ASMScene(scene)"/>
        public static Scene ASMScene(this GameObject gameObject, out Scene scene) =>
            scene = ASMScene(gameObject);

        /// <inheritdoc cref="ASMScene(scene)"/>
        public static Scene ASMScene(this Component component) =>
            component && component.gameObject
            ? ASMScene(component.gameObject)
            : null;

        /// <inheritdoc cref="ASMScene(scene)"/>
        public static Scene ASMScene(this GameObject gameObject) =>
            gameObject
            ? ASMScene(gameObject.scene)
            : null;

        /// <inheritdoc cref="ASMScene(scene)"/>
        public static bool ASMScene(this scene thisScene, out Scene scene) =>
            scene = ASMScene(thisScene);

        /// <summary>Gets the associated ASM <see cref="Scene"/>.</summary>
        public static Scene ASMScene(this scene scene)
        {

            if (!scene.IsValid())
                return null;
            else if (scene.handle == SceneManager.runtime.dontDestroyOnLoadScene.handle)
                return SceneManager.runtime.dontDestroyOnLoad;
            else if (FallbackSceneUtility.IsFallbackScene(scene))
                return null;
            else
                return SceneManager.assets.scenes.FirstOrDefault(s => s && (s.path == scene.path || s.internalScene?.handle == scene.handle));

        }


#if UNITY_EDITOR

        /// <inheritdoc cref="ASMScene(SceneAsset)"/>
        public static bool ASMScene(this SceneAsset thisScene, out Scene scene) =>
           scene = Find(AssetDatabase.GetAssetPath(thisScene));

        /// <summary>Finds the asm representation of this <see cref="SceneAsset"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static Scene ASMScene(this SceneAsset scene) =>
            Find(AssetDatabase.GetAssetPath(scene));

#endif

        #endregion
        #region Evaluate scene state

        /// <summary>Evaluate the final scene list after startup.</summary>
        /// <param name="profile">The profile that would be used to run startup process with.</param>
        /// <param name="props">The startup props that would be used to run process with.</param>
        public static IEnumerable<Scene> EvaluateFinalSceneList(Profile profile, App.StartupProps props)
        {

            if (props.openCollection)
                if (props.runStartupProcessWhenPlayingCollection)
                    return EvaluateFinalSceneList(profile.startupCollections.Except(props.openCollection).Concat(props.openCollection));
                else
                    return EvaluateFinalSceneList(Enumerable.Repeat(props.openCollection, 1));

            return EvaluateFinalSceneList(profile.startupCollections);

        }

        /// <summary>Evaluate the final scene list after opening a sequence of collections.</summary>
        /// <param name="collections">The sequence of collections that would be opened.</param>
        public static IEnumerable<Scene> EvaluateFinalSceneList(IEnumerable<SceneCollection> collections)
        {

            //Debug.Log("Collections that should be open: " + string.Join(", ", collections.Select(c => c.title)));

            if (collections.Count() == 0)
                return Enumerable.Empty<Scene>();
            if (collections.Count() == 1)
                return collections.ElementAt(0).scenesToAutomaticallyOpen;
            else
            {

                var finalCollection = collections.Last();
                var initialCollectionScenes = collections.Except(finalCollection).SelectMany(c => c.scenesToAutomaticallyOpen.Select(s => (collection: c, scene: s)));
                var finalCollectionScenes = finalCollection.scenesToAutomaticallyOpen;

                var remainingInitialScenes = initialCollectionScenes.Where(s => s.scene.EvalOpenAsPersistent(s.collection, finalCollection)).Select(s => s.scene);

                return remainingInitialScenes.Concat(finalCollectionScenes).Distinct();

            }

        }


        #endregion
        #region Enable / disable

        /// <summary>Sets all root objects as enabled / disabled.</summary>
        /// <remarks>Only has an effect if scene is open.</remarks>
        public static void SetEnabled(this Scene scene, bool isEnabled)
        {
            if (scene.isOpen)
                foreach (var obj in scene.GetRootGameObjects())
                    obj.SetActive(isEnabled);
        }

        /// <summary>Sets all root objects as enabled.</summary>
        /// <remarks>Only has an effect if scene is open.</remarks>
        public static void Enable(this Scene scene) => SetEnabled(scene, true);

        /// <summary>Sets all root objects as disabled.</summary>
        /// <remarks>Only has an effect if scene is open.</remarks>
        public static void Disable(this Scene scene) => SetEnabled(scene, false);

        #endregion

#if UNITY_EDITOR

        #region Move scene in hierarchy

        /// <summary>Moves the scene before another scene in the heirarchy.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void MoveBefore(Scene sceneToMove, Scene otherScene)
        {

            if (!sceneToMove.isOpen || !otherScene.isOpen)
                return;

            EditorSceneManager.MoveSceneBefore(sceneToMove.internalScene.Value, otherScene.internalScene.Value);

        }

        /// <summary>Moves the scene after another scene.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void MoveAfter(Scene sceneToMove, Scene otherScene)
        {

            if (!sceneToMove.isOpen || !otherScene.isOpen)
                return;

            EditorSceneManager.MoveSceneAfter(sceneToMove.internalScene.Value, otherScene.internalScene.Value);

        }

        /// <summary>Moves the scene to the top in the hierarchy.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void MoveToTop(Scene sceneToMove)
        {
            if (!sceneToMove.isOpen)
                return;

            var allScenes = GetAllOpenUnityScenes().ToArray();
            if (allScenes.Length == 0 || allScenes[0] == sceneToMove.internalScene.Value)
                return;

            EditorSceneManager.MoveSceneBefore(sceneToMove.internalScene.Value, allScenes[0]);
        }

        /// <summary>Moves the scene to the bottom in the hierarchy.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void MoveToBottom(Scene sceneToMove)
        {

            if (!sceneToMove.isOpen)
                return;

            var allScenes = GetAllOpenUnityScenes().ToArray();
            if (allScenes.Length == 0 || allScenes[allScenes.Length - 1] == sceneToMove.internalScene.Value)
                return;

            EditorSceneManager.MoveSceneAfter(sceneToMove.internalScene.Value, allScenes[allScenes.Length - 1]);

        }

        #endregion
        #region Split

        [MenuItem("GameObject/Move game objects to new scene...", false)]
        static void MoveToNewSceneItem() =>
            MoveToNewScene(Selection.objects.OfType<GameObject>().ToArray());

        [MenuItem("GameObject/Move game objects to new scene...", true)]
        static bool ValidateMoveToNewSceneItem() =>
            Selection.objects.Any();

        /// <summary>Moves the object to a new scene.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void MoveToNewScene(params GameObject[] objects)
        {

            if (objects?.Length == 0)
                throw new ArgumentException(nameof(objects));

            Undo.SetCurrentGroupName("Split scene");
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            foreach (var obj in objects)
            {
                Undo.SetTransformParent(obj.transform, null, true, "Set parent to null");
                Undo.MoveGameObjectToScene(obj, newScene, "Move object to scene");
            }

            EditorSceneManager.MarkSceneDirty(newScene);

            Undo.CollapseUndoOperations(group);

#if UNITY_2022_1_OR_NEWER

            Undo.undoRedoEvent += OnUndo;

            void OnUndo(in UndoRedoInfo undo)
            {
                if (!undo.isRedo)
                {
                    EditorSceneManager.CloseScene(newScene, true);
                    Undo.undoRedoEvent -= OnUndo;
                }
            }

#endif

        }

        #endregion
        #region Merge

        static scene GetScene(int instanceID) =>
            GetAllOpenUnityScenes().FirstOrDefault(s => s.handle == instanceID);

        [InitializeOnLoadMethod]
        static void OnLoad() =>
            SceneManager.OnInitialized(HeirarchyMenuItem);

        static void HeirarchyMenuItem()
        {
            SceneHierarchyHooks.addItemsToSceneHeaderContextMenu += (e, scene) =>
            {

                var scenes = Selection.instanceIDs.Select(GetScene).Select(s => AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path)).NonNull().ToArray();
                var targetScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
                scenes = scenes.Except(targetScene).ToArray();

                //Debug.Log(targetScene.name + ", " + string.Join(",", scenes.Select(s => s.name)));

                e.AddSeparator("");

                if (!Application.isPlaying && targetScene && scenes.Any())
                    e.AddItem(new("Merge scenes..."), false, () =>
                    {
                        if (PromptMerge(targetScene.name, scenes.Select(s => s.name)))
                            MergeScenes(AssetDatabase.GetAssetPath(targetScene), scenes.Select(AssetDatabase.GetAssetPath).ToArray());
                    });
                else
                    e.AddDisabledItem(new("Merge scenes..."));

            };
        }

        static bool PromptMerge(string targetScene, IEnumerable<string> scenes) =>
             PromptUtility.Prompt("Merging scenes...", $"You are about to merge the following scenes:\n{string.Join("\n", scenes)}\n\nInto:\n{targetScene}\n\nScenes will be moved to recycle bin.\nAre you sure?");

        public static void MergeScenes(this Scene targetScene, params Scene[] scenes) =>
            _MergeScenes(targetScene, false, scenes);

        public static void MergeScenes(string targetScenePath, params string[] scenePaths) =>
            _MergeScenes(targetScenePath, false, scenePaths);

        public static void MergeScenesPreserveOriginal(this Scene targetScene, params Scene[] scenes) =>
            _MergeScenes(targetScene, true, scenes);

        public static void MergeScenesPreserveOriginal(string targetScenePath, params string[] scenePaths) =>
            _MergeScenes(targetScenePath, true, scenePaths);


        private static void _MergeScenes(this Scene targetScene, bool keep, params Scene[] scenes)
        {
            if (!targetScene)
                throw new ArgumentNullException(nameof(targetScene));

            if (scenes?.NonNull()?.Count() == 0)
                throw new InvalidOperationException("Cannot merge less than two scenes.");

            _MergeScenes(targetScene.path, keep, scenes.Select(s => s.path).ToArray());
        }

        /// <summary>Merges the scenes together.</summary>
        /// <remarks>Only available in editor.</remarks>
        private static void _MergeScenes(string targetScenePath, bool keep, params string[] scenePaths)
        {

            ValidateArgs();

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {

                EnsureOpen(targetScenePath, out var targetScene);

                //Move all objects, and close scenes
                foreach (var path in scenePaths)
                {

                    EnsureOpen(path, out var scene);

                    var objects = scene.GetRootGameObjects();
                    if (objects.Length > 0)
                    {

                        _ = new GameObject($"--{Path.GetFileNameWithoutExtension(path)}--");
                        foreach (var obj in objects)
                        {
                            obj.transform.SetParent(null, worldPositionStays: true);
                            sceneManager.MoveGameObjectToScene(obj, targetScene);
                            obj.transform.SetAsLastSibling();
                        }

                    }

                    EditorSceneManager.SaveScene(scene);
                    if (!EditorSceneManager.CloseScene(scene, true))
                        Debug.LogError("Could not close scene:\n" + path);

                }

                //Save target scene
                _ = EditorSceneManager.SaveScene(targetScene);

                if (!keep)
                    Remove();

            }

            void EnsureOpen(string path, out scene scene)
            {

                scene = sceneManager.GetSceneByPath(path);
                if (!scene.isLoaded)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            }

            void ValidateArgs()
            {

                if (Application.isPlaying)
                    throw new InvalidOperationException("Cannot merge scenes in play-mode.");

                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(targetScenePath))
                    throw new ArgumentException(nameof(targetScenePath));

                scenePaths = scenePaths.Where(s => AssetDatabase.LoadAssetAtPath<SceneAsset>(s)).Except(targetScenePath).OrderByDescending(s => s).ToArray();

                if (!scenePaths.Any())
                    throw new InvalidOperationException("Cannot merge less than two scenes.");

            }

            void Remove()
            {

                //Move scenes to recycle bin
                foreach (var path in scenePaths)
                {

                    if (!AssetDatabase.MoveAssetToTrash(path))
                        throw new InvalidOperationException("Something went wrong when moving scene to recycle bin.\n" + path);

                    SceneImportUtility.Unimport(new[] { path });

                }

            }

        }

        #endregion
        #region Import

        #region SceneAsset

        /// <inheritdoc cref="Import(string)"/>
        public static Scene Import(this SceneAsset scene) => Import(AssetDatabase.GetAssetPath(scene));

        /// <inheritdoc cref="Import(string)"/>
        public static IEnumerable<Scene> Import(this IEnumerable<SceneAsset> scene) =>
            Import(scene.Select(AssetDatabase.GetAssetPath).ToArray());

        #endregion
        #region path

        /// <summary>Imports the scene into ASM and returns it. Returns already imported scene if already imported.</summary>
        public static Scene Import(string scene) =>
            SceneImportUtility.Import(scene);

        /// <summary>Imports the scene into ASM and returns it. Returns already imported scene if already imported.</summary>
        public static IEnumerable<Scene> Import(params string[] scene) => SceneImportUtility.Import(scene);

        #endregion

        #endregion
        #region Unimport

        /// <inheritdoc cref="Unimport(string[])"/>
        public static void Unimport(this SceneAsset scene) => Unimport(AssetDatabase.GetAssetPath(scene));

        /// <inheritdoc cref="Unimport(string[])"/>
        public static void Unimport(this IEnumerable<SceneAsset> scene) => Unimport(scene.Select(AssetDatabase.GetAssetPath).ToArray());

        /// <summary>Unimports the scene from ASM. No effect if scene not imported.</summary>
        public static void Unimport(params string[] scene) => SceneImportUtility.Unimport(scene);

        /// <inheritdoc cref="Unimport(string[])"/>
        public static void Unimport(this Scene scene) => Unimport(scene.path);

        /// <inheritdoc cref="Unimport(string[])"/>
        public static void Unimport(this IEnumerable<Scene> scene) => SceneImportUtility.Unimport(scene);

        /// <summary>Unimports the scene from ASM. No effect if scene not imported.</summary>
        public static void Unimport(params Scene[] scene) => SceneImportUtility.Unimport(scene);

        #endregion
        #region Add script

        static Scene activeScene;
        static void OpenScene(Scene scene, out bool wasAlreadyOpen)
        {

            if (Application.isPlaying)
                throw new InvalidOperationException("Cannot save scene after modification if we're in play mode!");

            if (!scene)
                throw new ArgumentNullException(nameof(scene));

            wasAlreadyOpen = scene.internalScene?.isLoaded ?? false;
            if (scene.isPreloaded)
                throw new InvalidOperationException("Cannot add script to preloaded scene.");

            if (wasAlreadyOpen)
                return;

            activeScene = SceneManager.runtime.activeScene;

            var uScene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Additive);
            scene.internalScene = uScene;
            scene.Activate();

        }

        static void CloseScene(Scene scene, bool wasAlreadyOpen)
        {

            if (!wasAlreadyOpen && scene.internalScene.HasValue && scene.internalScene.Value.isLoaded)
            {
                EditorSceneManager.SaveScene(scene.internalScene.Value);
                EditorSceneManager.CloseScene(scene.internalScene.Value, true);
            }

            if (activeScene)
                activeScene.Activate();

        }

        /// <summary>Adds a script to this scene. If scene is closed, it will be temporarily opened.</summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="scene"></param>
        /// <param name="configure"></param>
        /// <remarks>Only available in editor and outside of play-mode.</remarks>
        public static void AddScript<T>(this Scene scene, Action<T> configure = null) where T : Component, new()
        {

            OpenScene(scene, out var wasAlreadyOpen);

            var obj = new GameObject(typeof(T).Name);
            var t = obj.AddComponent<T>();
            configure?.Invoke(t);

            CloseScene(scene, wasAlreadyOpen);

        }

        /// <summary>Removes a script from this scene.</summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="scene"></param>
        /// <remarks>Only available in editor and outside of play-mode.</remarks>
        public static void RemoveScript<T>(this Scene scene, bool removeGameObject = false) where T : Component
        {

            OpenScene(scene, out var wasAlreadyOpen);

            var objs = scene.FindObjects<T>();
            foreach (var obj in objs)
            {

                if (!obj || !obj.gameObject)
                    continue;

                if (removeGameObject)
                    Object.DestroyImmediate(obj.gameObject, false);
                else
                    Object.DestroyImmediate(obj, false);

            }

            CloseScene(scene, wasAlreadyOpen);

        }

        #endregion
        #region Asset context menu

        const int basePriority = 200;

        #region Merge scenes

        [MenuItem("Assets/Advanced Scene Manager/Merge scenes...", validate = true)]
        static bool ValidateMergeSceneItem() =>
           !Application.isPlaying && Selection.objects.OfType<SceneAsset>().Count() > 1;

        [MenuItem("Assets/Advanced Scene Manager/Merge scenes...", priority = basePriority)]
        static void MergeSceneItem()
        {

            var scenes = Selection.objects.OfType<SceneAsset>().ToArray();
            var targetScene = scenes.FirstOrDefault();
            scenes = scenes.Except(targetScene).ToArray();

            if (PromptMerge(targetScene.name, scenes.Select(s => s.name)))
                MergeScenes(AssetDatabase.GetAssetPath(targetScene), scenes.Select(AssetDatabase.GetAssetPath).ToArray());

        }

        #endregion
        #region Import

        [MenuItem("Assets/Advanced Scene Manager/Import scenes...", validate = true)]
        static bool ValidateImportMenuItem() =>
            IsNotPlaying() &&
            GetScenePaths().Any(SceneImportUtility.StringExtensions.IsValidSceneToImport);

        [MenuItem("Assets/Advanced Scene Manager/Import scenes...", priority = basePriority + 20)]
        static void ImportMenuItem()
        {
            var scenes = GetScenePaths().Where(SceneImportUtility.StringExtensions.IsValidSceneToImport);
            SceneImportUtility.Import(scenes);
        }

        #endregion
        #region Unimport

        [MenuItem("Assets/Advanced Scene Manager/Unimport scenes...", validate = true)]
        static bool ValidateUnimportMenuItem() =>
           IsNotPlaying() && GetImportedScenes().Any();

        [MenuItem("Assets/Advanced Scene Manager/Unimport scenes...", priority = basePriority + 21)]
        static void UnimportMenuItem() =>
            Unimport(GetImportedScenes());

        #endregion
        #region Whitelist

        [MenuItem("Assets/Advanced Scene Manager/Add to whitelist...", validate = true)]
        static bool ValidateWhitelistFolderItem() => ValidateFolderOrSceneAssets();

        [MenuItem("Assets/Advanced Scene Manager/Add to whitelist...", priority = basePriority + 40)]
        static void WhitelistFolderItem()
        {

            if (GetFolder(out var folder))
                BlocklistUtility.whitelist.Add(folder);

            foreach (var scene in GetScenes())
                BlocklistUtility.whitelist.Add(AssetDatabase.GetAssetPath(scene));

        }

        #endregion
        #region Blacklist

        [MenuItem("Assets/Advanced Scene Manager/Add to blacklist...", validate = true)]
        static bool ValidateBlacklistFolderItem() => ValidateFolderOrSceneAssets();

        [MenuItem("Assets/Advanced Scene Manager/Add to blacklist...", priority = basePriority + 41)]
        static void BlacklistFolderItem()
        {

            if (GetFolder(out var folder))
                BlocklistUtility.blacklist.Add(folder);

            foreach (var scene in GetScenes())
                BlocklistUtility.blacklist.Add(AssetDatabase.GetAssetPath(scene));

        }

        #endregion
        #region Dynamic collection

        [MenuItem("Assets/Advanced Scene Manager/Create dynamic collection...", validate = true)]
        static bool ValidateCreateDynamicCollectionItem() => Profile.current && ValidateFolderOrSceneAssets();

        [MenuItem("Assets/Advanced Scene Manager/Create dynamic collection...", priority = basePriority + 60)]
        static void CreateDynamicCollectionItem()
        {

            if (GetFolder(out var folder))
            {
                var name = Path.GetFileName(folder);
                Profile.current.CreateDynamicCollection(folder, name);
            }

            foreach (var scene in GetScenePaths())
            {
                var name = Path.GetFileNameWithoutExtension(scene);
                Profile.current.CreateDynamicCollection(scene, name);
            }

        }

        #endregion

        static bool IsNotPlaying() => !Application.isPlaying;
        static bool IsFolderSelected() => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(Selection.activeObject));
        static bool HasSceneAssetsSelected() => Selection.objects.OfType<SceneAsset>().Any();

        static bool ValidateFolderOrSceneAssets() => IsNotPlaying() && (IsFolderSelected() || HasSceneAssetsSelected());

        static SceneAsset[] GetScenes() =>
            Selection.objects.OfType<SceneAsset>().ToArray();

        static string[] GetScenePaths() =>
            Selection.objects.OfType<SceneAsset>().Select(AssetDatabase.GetAssetPath).ToArray();

        static Scene[] GetImportedScenes() =>
            Selection.objects.OfType<SceneAsset>().Select(s => s.ASMScene()).NonNull().ToArray();

        static bool GetFolder(out string folder)
        {
            folder = AssetDatabase.GetAssetPath(Selection.activeObject);
            return AssetDatabase.IsValidFolder(folder);
        }

        #endregion

#endif

    }

}
