#if CYCLONEGAMES_HAS_ADDRESSABLES
using System.IO;

using UnityEngine;
using UnityEngine.AddressableAssets;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Resolves product-owned Addressables version metadata paths without inspecting provider internals.
    /// </summary>
    internal static class AddressablesVersionPathHelper
    {
        private const string VERSION_FILE_NAME = "AddressablesVersion.json";
        private const string ADDRESSABLES_CACHE_FOLDER = "com.unity.addressables";

        public static string GetPersistentVersionPath()
        {
            return Path.Combine(
                Application.persistentDataPath,
                ADDRESSABLES_CACHE_FOLDER,
                VERSION_FILE_NAME);
        }

        public static string GetStreamingAssetsVersionPath()
        {
            return Path.Combine(
                Addressables.PlayerBuildDataPath,
                VERSION_FILE_NAME);
        }

    }
}
#endif
