#if VCONTAINER_PRESENT
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using System;

namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    /// <summary>
    /// Helper methods for loading input configuration from AssetManagement (YooAsset/Addressables).
    /// All methods work directly with IAssetPackage, without requiring IObjectResolver or IAssetModule.
    /// </summary>
    public static class InputSystemAssetManagementHelper
    {
        /// <summary>
        /// Creates a default config loader from AssetManagement package.
        /// Note: User config is always loaded from PersistentData automatically by InputSystemVContainerInstaller.
        /// This method only creates the default config loader.
        /// Supports both TextAsset (for Addressables/Resources) and RawFile (for YooAsset) loading.
        /// </summary>
        /// <param name="package">AssetManagement package instance. Must be provided directly, not resolved from DI.</param>
        /// <param name="defaultConfigLocation">Location of default config in AssetManagement (e.g., "input_config.yaml" or "Assets/Config/input_config.yaml")</param>
        /// <param name="useTextAsset">If true, loads as TextAsset (works with all providers). If false, tries RawFile first, then falls back to TextAsset.</param>
        /// <returns>Default config loader function that doesn't require resolver</returns>
        public static Func<UniTask<string>> CreateDefaultConfigLoader(IAssetPackage package, string defaultConfigLocation, bool useTextAsset = false)
        {
            if (package == null)
            {
                CycloneGames.Logger.CLogger.LogError("[InputSystemAssetManagementHelper] Package cannot be null.");
                return async () => null;
            }

            return async () =>
            {
                // Try TextAsset first if requested, or as fallback
                if (useTextAsset)
                {
                    return await LoadConfigAsTextAsset(package, defaultConfigLocation);
                }

                // Try RawFile first (YooAsset), fallback to TextAsset (Addressables/Resources)
                try
                {
                    var rawFileHandle = package.LoadRawFileAsync(defaultConfigLocation);
                    await rawFileHandle.Task;

                    if (rawFileHandle.IsDone && string.IsNullOrEmpty(rawFileHandle.Error))
                    {
                        string content = rawFileHandle.ReadText();
                        rawFileHandle.Dispose();
                        CycloneGames.Logger.CLogger.LogInfo($"[InputSystemAssetManagementHelper] Loaded config as RawFile from: {defaultConfigLocation}");
                        return content;
                    }
                    else
                    {
                        rawFileHandle.Dispose();
                        // Fallback to TextAsset
                        CycloneGames.Logger.CLogger.LogInfo($"[InputSystemAssetManagementHelper] RawFile loading failed, trying TextAsset: {defaultConfigLocation}");
                        return await LoadConfigAsTextAsset(package, defaultConfigLocation);
                    }
                }
                catch (System.Exception e)
                {
                    CycloneGames.Logger.CLogger.LogWarning($"[InputSystemAssetManagementHelper] RawFile loading exception, trying TextAsset: {e.Message}");
                    return await LoadConfigAsTextAsset(package, defaultConfigLocation);
                }
            };
        }

        private static async UniTask<string> LoadConfigAsTextAsset(IAssetPackage package, string location)
        {
            try
            {
                using (var handle = package.LoadAssetAsync<UnityEngine.TextAsset>(location))
                {
                    await handle.Task;

                    if (handle.Asset != null)
                    {
                        string content = handle.Asset.text;
                        CycloneGames.Logger.CLogger.LogInfo($"[InputSystemAssetManagementHelper] Loaded config as TextAsset from: {location}");
                        return content;
                    }
                    else
                    {
                        CycloneGames.Logger.CLogger.LogWarning($"[InputSystemAssetManagementHelper] Failed to load TextAsset from '{location}': Asset is null");
                        return null;
                    }
                }
            }
            catch (System.Exception e)
            {
                CycloneGames.Logger.CLogger.LogError($"[InputSystemAssetManagementHelper] Exception loading TextAsset from '{location}': {e.Message}");
                return null;
            }
        }


        /// <summary>
        /// Creates a loader function that loads config from AssetManagement package.
        /// Useful for hot-update scenarios where you can call this after the package is ready.
        /// Supports both TextAsset (for Addressables/Resources) and RawFile (for YooAsset) loading.
        /// </summary>
        /// <param name="package">AssetManagement package instance (IAssetPackage)</param>
        /// <param name="configLocation">Location of config in AssetManagement</param>
        /// <param name="useTextAsset">If true, loads as TextAsset (works with all providers). If false, tries RawFile first, then falls back to TextAsset.</param>
        /// <returns>Async function that loads and returns config content</returns>
        public static Func<UniTask<string>> CreateConfigLoader(IAssetPackage package, string configLocation, bool useTextAsset = false)
        {
            if (package == null)
            {
                CycloneGames.Logger.CLogger.LogError("[InputSystemAssetManagementHelper] Package cannot be null.");
                return async () => null;
            }

            return async () =>
            {
                // Try TextAsset first if requested, or as fallback
                if (useTextAsset)
                {
                    return await LoadConfigAsTextAsset(package, configLocation);
                }

                // Try RawFile first (YooAsset), fallback to TextAsset (Addressables/Resources)
                try
                {
                    var rawFileHandle = package.LoadRawFileAsync(configLocation);
                    await rawFileHandle.Task;

                    if (rawFileHandle.IsDone && string.IsNullOrEmpty(rawFileHandle.Error))
                    {
                        string content = rawFileHandle.ReadText();
                        rawFileHandle.Dispose();
                        CycloneGames.Logger.CLogger.LogInfo($"[InputSystemAssetManagementHelper] Loaded config as RawFile from: {configLocation}");
                        return content;
                    }
                    else
                    {
                        rawFileHandle.Dispose();
                        // Fallback to TextAsset
                        CycloneGames.Logger.CLogger.LogInfo($"[InputSystemAssetManagementHelper] RawFile loading failed, trying TextAsset: {configLocation}");
                        return await LoadConfigAsTextAsset(package, configLocation);
                    }
                }
                catch (System.Exception e)
                {
                    CycloneGames.Logger.CLogger.LogWarning($"[InputSystemAssetManagementHelper] RawFile loading exception, trying TextAsset: {e.Message}");
                    return await LoadConfigAsTextAsset(package, configLocation);
                }
            };
        }

        private static async UniTask<(bool, string)> LoadConfigFromUriAsync(string uri)
        {
            using (UnityEngine.Networking.UnityWebRequest uwr = UnityEngine.Networking.UnityWebRequest.Get(uri))
            {
                try
                {
                    var asyncOperation = uwr.SendWebRequest();
                    while (!asyncOperation.isDone)
                    {
                        await UniTask.Yield();
                    }

                    if (uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        return (true, uwr.downloadHandler.text);
                    }
                    return (false, null);
                }
                catch
                {
                    return (false, null);
                }
            }
        }
    }
}
#endif