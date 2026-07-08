#if CYCLONEGAMES_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

using Cysharp.Threading.Tasks;
using CycloneGames.Logger;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class AddressablesUtils
    {
        /// <summary>
        /// Attaches a CancellationToken to an Addressables operation.
        /// If the token is cancelled, the handle is released.
        /// </summary>
        public static async UniTask<AsyncOperationHandle<T>> WithCancellation<T>(this AsyncOperationHandle<T> handle, CancellationToken cancellationToken)
        {
            try
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                throw;
            }
            return handle;
        }
        
        public static async UniTask<AsyncOperationHandle> WithCancellation(this AsyncOperationHandle handle, CancellationToken cancellationToken)
        {
            try
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                throw;
            }
            return handle;
        }
    }

#if UNITY_EDITOR
    [InitializeOnLoad]
    internal static class AddressablesEditorPlayModeGuard
    {
        static AddressablesEditorPlayModeGuard()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                AddressablesEditorCleanupUtility.PrepareForPlayModeExit();
            }
        }
    }

    internal static class AddressablesEditorCleanupUtility
    {
        private const BindingFlags STATIC_PRIVATE = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags INSTANCE_PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly List<AsyncOperationHandle> _invalidSceneHandles = new List<AsyncOperationHandle>(8);
        private static readonly List<AsyncOperationHandle> _sceneHandles = new List<AsyncOperationHandle>(8);
        private static readonly List<object> _resultKeysToRemove = new List<object>(8);

        public static void RemoveInvalidTrackedSceneHandles()
        {
            try
            {
                var implField = typeof(Addressables).GetField("s_AddressablesImpl", STATIC_PRIVATE);
                var impl = implField?.GetValue(null);
                if (impl == null)
                {
                    return;
                }

                var sceneInstancesField = impl.GetType().GetField("m_SceneInstances", INSTANCE_PRIVATE);
                if (sceneInstancesField?.GetValue(impl) is not HashSet<AsyncOperationHandle> sceneHandles)
                {
                    return;
                }

                _invalidSceneHandles.Clear();
                foreach (var handle in sceneHandles)
                {
                    if (!handle.IsValid())
                    {
                        _invalidSceneHandles.Add(handle);
                    }
                }

                for (var i = 0; i < _invalidSceneHandles.Count; i++)
                {
                    sceneHandles.Remove(_invalidSceneHandles[i]);
                }
            }
            catch (Exception ex)
            {
                CLogger.LogWarning($"[AddressablesEditorCleanupUtility] Failed to clean invalid scene handles: {ex.Message}");
            }
            finally
            {
                _invalidSceneHandles.Clear();
            }
        }

        public static void PrepareForPlayModeExit()
        {
            try
            {
                var impl = GetAddressablesImpl();
                if (impl == null)
                {
                    return;
                }

                var sceneHandles = GetSceneInstances(impl);
                if (sceneHandles == null || sceneHandles.Count == 0)
                {
                    return;
                }

                var resultToHandle = GetResultToHandle(impl);
                var singleLoadedScene = GetSingleLoadedScene();

                _sceneHandles.Clear();
                foreach (var handle in sceneHandles)
                {
                    _sceneHandles.Add(handle);
                }

                sceneHandles.Clear();

                for (var i = 0; i < _sceneHandles.Count; i++)
                {
                    var handle = _sceneHandles[i];
                    if (!handle.IsValid() || ShouldSkipSceneReleaseOnPlayModeExit(handle, singleLoadedScene))
                    {
                        RemoveHandleFromResultMap(resultToHandle, handle);
                    }
                }
            }
            catch (Exception ex)
            {
                CLogger.LogWarning($"[AddressablesEditorCleanupUtility] Failed to prepare play mode exit: {ex.Message}");
            }
            finally
            {
                _sceneHandles.Clear();
                _resultKeysToRemove.Clear();
            }
        }

        private static object GetAddressablesImpl()
        {
            var implField = typeof(Addressables).GetField("s_AddressablesImpl", STATIC_PRIVATE);
            return implField?.GetValue(null);
        }

        private static HashSet<AsyncOperationHandle> GetSceneInstances(object impl)
        {
            var sceneInstancesField = impl.GetType().GetField("m_SceneInstances", INSTANCE_PRIVATE);
            return sceneInstancesField?.GetValue(impl) as HashSet<AsyncOperationHandle>;
        }

        private static Dictionary<object, AsyncOperationHandle> GetResultToHandle(object impl)
        {
            var resultToHandleField = impl.GetType().GetField("m_resultToHandle", INSTANCE_PRIVATE);
            return resultToHandleField?.GetValue(impl) as Dictionary<object, AsyncOperationHandle>;
        }

        private static Scene GetSingleLoadedScene()
        {
            Scene singleLoadedScene = default;
            var loadedSceneCount = 0;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                loadedSceneCount++;
                singleLoadedScene = scene;
                if (loadedSceneCount > 1)
                {
                    return default;
                }
            }

            return loadedSceneCount == 1 ? singleLoadedScene : default;
        }

        private static bool ShouldSkipSceneReleaseOnPlayModeExit(AsyncOperationHandle handle, Scene singleLoadedScene)
        {
            if (!singleLoadedScene.IsValid())
            {
                return false;
            }

            if (!TryGetScene(handle, out var scene))
            {
                return true;
            }

            return scene == singleLoadedScene;
        }

        private static bool TryGetScene(AsyncOperationHandle handle, out Scene scene)
        {
            scene = default;
            if (!handle.IsValid())
            {
                return false;
            }

            try
            {
                var sceneHandle = handle.Convert<SceneInstance>();
                scene = sceneHandle.Result.Scene;
                return scene.IsValid();
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveHandleFromResultMap(Dictionary<object, AsyncOperationHandle> resultToHandle, AsyncOperationHandle handle)
        {
            if (resultToHandle == null || resultToHandle.Count == 0)
            {
                return;
            }

            _resultKeysToRemove.Clear();
            foreach (var kvp in resultToHandle)
            {
                if (kvp.Value.Equals(handle))
                {
                    _resultKeysToRemove.Add(kvp.Key);
                }
            }

            for (var i = 0; i < _resultKeysToRemove.Count; i++)
            {
                resultToHandle.Remove(_resultKeysToRemove[i]);
            }
        }
    }
#endif
}
#endif
