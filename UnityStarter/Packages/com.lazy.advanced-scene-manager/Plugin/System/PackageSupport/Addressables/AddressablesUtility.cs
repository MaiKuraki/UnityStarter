#if ADDRESSABLES && UNITY_EDITOR

using System.Collections.Generic;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AdvancedSceneManager.PackageSupport.Addressables.Editor
{

    [InitializeOnLoad]
    static class AddressablesUtility
    {

        public static AddressableAssetSettings settings { get; private set; }

        static AddressablesUtility() =>
            SceneManager.OnInitialized(() =>
            {

                CoroutineUtility.Run(() =>
                {
                    Refresh();
                    ListenToAddressablesChange();
                }, when: IsAddressablesInitialized);

                bool IsAddressablesInitialized()
                {

                    if (EditorApplication.isUpdating || EditorApplication.isCompiling)
                        return false;

                    if (settings)
                        return true;
                    else
                        return settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

                }

            });

        static void ListenToAddressablesChange()
        {

            if (settings == null)
                return;

            settings.OnModification -= OnModification;
            settings.OnModification += OnModification;

            static void OnModification(AddressableAssetSettings s, AddressableAssetSettings.ModificationEvent e, object obj)
            {

                if (obj is AddressableAssetEntry)
                    OnModification((AddressableAssetEntry)obj);

                else if (obj is IEnumerable<AddressableAssetEntry> entries)
                    foreach (var entry in entries)
                        OnModification(entry);

                void OnModification(AddressableAssetEntry entry)
                {

                    if (SceneManager.assets.scenes.TryFind(entry.address, out var scene) && !Extensions.updating.Contains(scene))
                    {
                        scene.SetSceneLoader<SceneLoader>();
                        Debug.Log(entry?.GetType()?.Name + ": " + entry?.ToString() + "\n(" + scene.name + ")");
                    }

                }

            }

        }

        static void Refresh()
        {

            if (!settings)
                return;

            foreach (var scene in SceneManager.assets.scenes)
            {

                Extensions.updating.Add(scene);

                var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(scene.path));
                if (scene.isAddressable && entry is null)
                    scene.AddToAddressables();
                else if (!scene.isAddressable && entry is not null)
                    scene.RemoveFromAddressables();

                if (entry is not null)
                    UpgradeToNewAddress(entry, scene);

                Extensions.updating.Remove(scene);

            }

            BuildUtility.UpdateSceneList();

        }

        static void UpgradeToNewAddress(AddressableAssetEntry entry, Scene scene)
        {
            if (entry.address == scene.id)
                entry.SetAddress(scene.address);
        }

    }

}
#endif
