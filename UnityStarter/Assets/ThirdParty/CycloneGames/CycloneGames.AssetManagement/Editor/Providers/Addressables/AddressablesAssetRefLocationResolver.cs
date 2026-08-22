#if CYCLONEGAMES_HAS_ADDRESSABLES
using CycloneGames.AssetManagement.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace CycloneGames.AssetManagement.Editor.Providers.Addressables
{
    /// <summary>
    /// Derives an Addressables runtime location from the project's default AddressableAssetSettings.
    /// <para>
    /// The address is resolved by the entry's GUID, so renaming or moving the prefab does not change it unless
    /// the address itself is authored from the path. An entry with no explicit address resolves by its asset
    /// path, which is also the key Addressables uses at runtime in that case.
    /// </para>
    /// </summary>
    public sealed class AddressablesAssetRefLocationResolver : IAssetRefLocationResolver
    {
        public int Priority => 0;

        public string ResolveLocation(string assetGuid, string assetPath)
        {
            if (string.IsNullOrEmpty(assetGuid))
            {
                return null;
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                return null;
            }

            AddressableAssetEntry entry = settings.FindAssetEntry(assetGuid);
            if (entry == null)
            {
                return null;
            }

            string address = entry.address;
            if (!string.IsNullOrWhiteSpace(address))
            {
                return address;
            }

            // An Addressables entry without an explicit address is keyed by its asset path.
            return string.IsNullOrEmpty(assetPath)
                ? AssetDatabase.GUIDToAssetPath(assetGuid)
                : assetPath;
        }
    }
}
#endif
