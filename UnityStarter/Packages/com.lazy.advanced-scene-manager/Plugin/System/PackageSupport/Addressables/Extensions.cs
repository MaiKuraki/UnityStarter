#if ADDRESSABLES

using System.Collections;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using AdvancedSceneManager.Editor.Utility;
using static AdvancedSceneManager.PackageSupport.Addressables.Editor.AddressablesUtility;
#endif

namespace AdvancedSceneManager.PackageSupport.Addressables
{

    static class Extensions
    {

        public static readonly List<Scene> updating = new();
        public static void OnAddressablesChanged(Scene scene)
        {

#if UNITY_EDITOR

            if (!scene)
                return;

            if (scene.isAddressable)
                AddToAddressables(scene);
            else
                RemoveFromAddressables(scene);


#endif

        }

#if UNITY_EDITOR

        /// <summary>Adds scene to addressables.</summary>
        internal static void AddToAddressables(this Scene scene)
        {

            //Addressable scenes cannot be added to build list and addressables has a
            //check before creating entry if it is, so we'll need to remove it beforehand.
            BuildUtility.UpdateSceneList();

            if (!updating.Contains(scene))
                updating.Add(scene);

            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(scene.path), settings.DefaultGroup, postEvent: false);

            entry.SetLabel("ASM", true, true);
            entry.SetAddress(scene.address);

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryCreated, entry, postEvent: true, settingsModified: true);

            updating.Remove(scene);
            BuildUtility.UpdateSceneList();

        }

        /// <summary>Removes scene from addressables.</summary>
        internal static void RemoveFromAddressables(this Scene scene)
        {

            if (!updating.Contains(scene))
                updating.Add(scene);

            _ = settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(scene.path));

            //Addressable scenes cannot be added to build list and addressables has a
            //check before creating entry if it is, so we'll need to remove it beforehand.
            updating.Remove(scene);
            BuildUtility.UpdateSceneList();

        }

#endif

        /// <summary>Returns a coroutine that returns when <see cref="AsyncOperation.isDone"/> becomes <see langword="true"/>. <paramref name="onProgress"/> will be called every frame with <see cref="AsyncOperation.progress"/>.</summary>
        public static GlobalCoroutine ReportProgress(this AsyncOperationHandle<SceneInstance> asyncOperation, SceneOperationKind kind, Scene scene, SceneOperation operation)
        {

            return Coroutine().StartCoroutine();

            IEnumerator Coroutine()
            {

                float lastProgress = -1f;

                while (!asyncOperation.IsDone)
                {

                    float progress = asyncOperation.PercentComplete;

                    if (!Mathf.Approximately(lastProgress, progress))
                    {

                        lastProgress = progress;

                        if (operation is not null && !operation.progressScope.isRegistered)
                            operation.progressScope.SetSubProgress(kind, scene, asyncOperation.PercentComplete);

                        LoadingScreenUtility.ReportProgress(new SceneLoadProgressData(asyncOperation.PercentComplete, kind, operation, scene));

                    }

                    yield return null;

                }

                if (operation is not null && !operation.progressScope.isRegistered)
                    operation.progressScope.SetSubProgress(kind, scene, 1f);

                LoadingScreenUtility.ReportProgress(new SceneLoadProgressData(1f, kind, operation, scene));


            }

        }

    }

}
#endif
