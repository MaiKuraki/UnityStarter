#if VCONTAINER_PRESENT
using System;
using VContainer;
using VContainer.Unity;
using CycloneGames.Utility.Runtime;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using Unio;
using Unity.Collections;

namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    /// <summary>
    /// VContainer installer for CycloneGames.InputSystem.
    /// Registers InputManager and provides factory for IInputPlayer resolution.
    /// Supports both URI-based loading (StreamingAssets/PersistentData) and custom config loaders (e.g., AssetManagement).
    /// Supports delayed initialization for hot-update scenarios where AssetManagement packages may not be ready at registration time.
    /// </summary>
    public class InputSystemVContainerInstaller : IInstaller
    {
        private readonly string _defaultConfigFileName;
        private readonly string _userConfigFileName;
        private readonly Func<UniTask<string>> _defaultConfigLoader;
        private readonly Func<IObjectResolver, UniTask> _postInitCallback;
        private readonly bool _autoInitialize;

        /// <summary>
        /// Creates installer with URI-based config loading (StreamingAssets/PersistentData).
        /// </summary>
        /// <param name="defaultConfigFileName">Default config file name (e.g., "input_config.yaml")</param>
        /// <param name="userConfigFileName">User config file name (e.g., "user_input_settings.yaml")</param>
        /// <param name="autoInitialize">If true, initializes automatically during container build. If false, use IInputSystemInitializer to initialize manually (for hot-update scenarios).</param>
        /// <param name="postInitCallback">Optional callback after initialization</param>
        public InputSystemVContainerInstaller(
            string defaultConfigFileName = "input_config.yaml",
            string userConfigFileName = "user_input_settings.yaml",
            bool autoInitialize = true,
            Func<IObjectResolver, UniTask> postInitCallback = null)
        {
            _defaultConfigFileName = defaultConfigFileName;
            _userConfigFileName = userConfigFileName;
            _autoInitialize = autoInitialize;
            _postInitCallback = postInitCallback;
        }

        /// <summary>
        /// Creates installer with custom default config loader (e.g., for AssetManagement, YooAsset, Addressables).
        /// User config is always loaded from PersistentData path (userConfigFileName).
        /// The loader function should work directly with IAssetPackage, without requiring IObjectResolver or IAssetModule.
        /// </summary>
        /// <param name="defaultConfigLoader">Async function to load default config content. Returns null if not found. Should not depend on IObjectResolver.</param>
        /// <param name="userConfigFileName">User config file name for PersistentData (e.g., "user_input_settings.yaml")</param>
        /// <param name="autoInitialize">If true, initializes automatically during container build. If false, use IInputSystemInitializer to initialize manually (for hot-update scenarios).</param>
        /// <param name="postInitCallback">Optional callback after initialization</param>
        public InputSystemVContainerInstaller(
            Func<UniTask<string>> defaultConfigLoader,
            string userConfigFileName = "user_input_settings.yaml",
            bool autoInitialize = true,
            Func<IObjectResolver, UniTask> postInitCallback = null)
        {
            _defaultConfigLoader = defaultConfigLoader;
            _userConfigFileName = userConfigFileName;
            _autoInitialize = autoInitialize;
            _postInitCallback = postInitCallback;
        }

        public void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(InputManager.Instance).As<InputManager>();
            builder.Register<IInputPlayerResolver>(container =>
            {
                return new InputPlayerResolver(InputManager.Instance);
            }, Lifetime.Singleton).As<IInputPlayerResolver>();

            // Register initializer for manual initialization (hot-update scenarios)
            builder.Register<IInputSystemInitializer>(container =>
            {
                return new InputSystemInitializer(
                    _defaultConfigFileName,
                    _userConfigFileName,
                    _defaultConfigLoader,
                    _postInitCallback
                );
            }, Lifetime.Singleton).As<IInputSystemInitializer>();

            // Auto-initialize if enabled
            if (_autoInitialize)
            {
                builder.RegisterBuildCallback(async resolver =>
                {
                    var initializer = resolver.Resolve<IInputSystemInitializer>();
                    await initializer.InitializeAsync(resolver);
                });
            }
        }

    }

    /// <summary>
    /// Initializer for InputSystem. Use this for manual initialization in hot-update scenarios.
    /// Supports cross-scene and cross-resolver configuration reloading.
    /// </summary>
    public interface IInputSystemInitializer
    {
        /// <summary>
        /// Initializes InputSystem with the configured loaders.
        /// </summary>
        UniTask InitializeAsync(IObjectResolver resolver);

        /// <summary>
        /// Reinitializes InputSystem from a new Package (e.g., after hot-update).
        /// Can be called from any scene/resolver after the package is ready.
        /// </summary>
        /// <param name="package">AssetManagement package instance. Use InputSystemAssetManagementHelper.CreateConfigLoader for type-safe access.</param>
        /// <param name="configLocation">Location of config in the package</param>
        /// <param name="saveToUserConfig">If true, saves the loaded config to user config file</param>
        UniTask ReinitializeFromPackageAsync(object package, string configLocation, bool saveToUserConfig = true);

        /// <summary>
        /// Updates InputManager configuration from YAML content (e.g., after hot-update or player key rebinding).
        /// </summary>
        /// <param name="yamlContent">New YAML configuration content</param>
        /// <param name="saveToUserConfig">If true, saves the new config to user config file</param>
        UniTask UpdateConfigurationAsync(string yamlContent, bool saveToUserConfig = false);

        /// <summary>
        /// Reloads user configuration from PersistentData file.
        /// Useful when player has modified key bindings and saved them.
        /// </summary>
        UniTask ReloadUserConfigurationAsync();
    }

    /// <summary>
    /// Resolver for IInputPlayer instances by player ID.
    /// Provides DI-friendly access to player input.
    /// </summary>
    public interface IInputPlayerResolver
    {
        IInputPlayer GetInputPlayer(int playerId);
        bool TryGetInputPlayer(int playerId, out IInputPlayer inputPlayer);
    }

    internal class InputSystemInitializer : IInputSystemInitializer
    {
        private readonly string _defaultConfigFileName;
        private readonly string _userConfigFileName;
        private readonly Func<UniTask<string>> _defaultConfigLoader;
        private readonly Func<IObjectResolver, UniTask> _postInitCallback;

        public InputSystemInitializer(
            string defaultConfigFileName,
            string userConfigFileName,
            Func<UniTask<string>> defaultConfigLoader,
            Func<IObjectResolver, UniTask> postInitCallback)
        {
            _defaultConfigFileName = defaultConfigFileName;
            _userConfigFileName = userConfigFileName;
            _defaultConfigLoader = defaultConfigLoader;
            _postInitCallback = postInitCallback;
        }

        public async UniTask InitializeAsync(IObjectResolver resolver)
        {
            string yamlContent = null;
            string userConfigFileName = _userConfigFileName ?? "user_input_settings.yaml";
            string userConfigUri = FilePathUtility.GetUnityWebRequestUri(userConfigFileName, UnityPathSource.PersistentData);
            bool loadedFromUserConfig = false;

            // Always try loading user config from PersistentData first
            (bool success, string content) = await LoadConfigFromUriAsync(userConfigUri);
            if (success && !string.IsNullOrEmpty(content))
            {
                yamlContent = content;
                loadedFromUserConfig = true;
                CycloneGames.Logger.CLogger.LogInfo($"[InputSystemInitializer] Loaded user config from PersistentData: {userConfigFileName}");
            }

            // Fallback to default config if user config not found
            if (string.IsNullOrEmpty(yamlContent))
            {
                if (_defaultConfigLoader != null)
                {
                    yamlContent = await _defaultConfigLoader();
                    if (!string.IsNullOrEmpty(yamlContent))
                    {
                        CycloneGames.Logger.CLogger.LogInfo("[InputSystemInitializer] Loaded default config from custom loader.");
                    }
                }
                else if (!string.IsNullOrEmpty(_defaultConfigFileName))
                {
                    var defaultUri = FilePathUtility.GetUnityWebRequestUri(_defaultConfigFileName, UnityPathSource.StreamingAssets);
                    (bool defaultSuccess, string defaultContent) = await LoadConfigFromUriAsync(defaultUri);
                    if (defaultSuccess && !string.IsNullOrEmpty(defaultContent))
                    {
                        yamlContent = defaultContent;
                        CycloneGames.Logger.CLogger.LogInfo($"[InputSystemInitializer] Loaded default config from StreamingAssets: {_defaultConfigFileName}");
                    }
                }

                if (string.IsNullOrEmpty(yamlContent))
                {
                    CycloneGames.Logger.CLogger.LogError("[InputSystemInitializer] Failed to load input configuration from both default and user sources.");
                    return;
                }
            }

            InputManager.Instance.Initialize(yamlContent, userConfigUri);

            if (!loadedFromUserConfig)
            {
                await InputManager.Instance.SaveUserConfigurationAsync();
                CycloneGames.Logger.CLogger.LogInfo($"[InputSystemInitializer] Saved default config to user config: {userConfigFileName}");
            }

            if (_postInitCallback != null)
            {
                await _postInitCallback(resolver);
            }
        }

        public async UniTask ReinitializeFromPackageAsync(object package, string configLocation, bool saveToUserConfig = true)
        {
            if (package == null)
            {
                CycloneGames.Logger.CLogger.LogError("[InputSystemInitializer] Cannot reinitialize: package is null.");
                return;
            }

            try
            {
                var assetPackage = package as IAssetPackage;
                if (assetPackage == null)
                {
                    CycloneGames.Logger.CLogger.LogError("[InputSystemInitializer] Package must implement IAssetPackage interface.");
                    return;
                }
                var loader = InputSystemAssetManagementHelper.CreateConfigLoader(
                    assetPackage,
                    configLocation
                );

                if (loader != null)
                {
                    string yamlContent = await loader();
                    if (!string.IsNullOrEmpty(yamlContent))
                    {
                        await UpdateConfigurationAsync(yamlContent, saveToUserConfig);
                        CycloneGames.Logger.CLogger.LogInfo($"[InputSystemInitializer] Reinitialized from package: {configLocation}");
                    }
                    else
                    {
                        CycloneGames.Logger.CLogger.LogWarning($"[InputSystemInitializer] Config content is empty from: {configLocation}");
                    }
                }
                else
                {
                    CycloneGames.Logger.CLogger.LogError("[InputSystemInitializer] Failed to create config loader. Ensure package is IAssetPackage type and AssetManagement is available.");
                }
            }
            catch (System.Exception e)
            {
                CycloneGames.Logger.CLogger.LogError($"[InputSystemInitializer] Exception loading config from package: {e.Message}. Ensure AssetManagement is available and package is IAssetPackage type.");
            }
        }

        public async UniTask UpdateConfigurationAsync(string yamlContent, bool saveToUserConfig = false)
        {
            if (string.IsNullOrEmpty(yamlContent))
            {
                CycloneGames.Logger.CLogger.LogError("[InputSystemInitializer] Cannot update configuration: YAML content is null or empty.");
                return;
            }

            string userConfigFileName = _userConfigFileName ?? "user_input_settings.yaml";
            string userConfigUri = FilePathUtility.GetUnityWebRequestUri(userConfigFileName, UnityPathSource.PersistentData);

            var inputManager = CycloneGames.InputSystem.Runtime.InputManager.Instance;

            if (inputManager != null)
            {
                try
                {
                    // Save to file if requested
                    if (saveToUserConfig)
                    {
                        byte[] yamlBytes = System.Text.Encoding.UTF8.GetBytes(yamlContent);
                        string filePath = new Uri(userConfigUri).LocalPath;
                        string directory = System.IO.Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                        {
                            System.IO.Directory.CreateDirectory(directory);
                        }
                        using var nativeBytes = new Unity.Collections.NativeArray<byte>(yamlBytes, Unity.Collections.Allocator.Temp);
                        await Unio.NativeFile.WriteAllBytesAsync(filePath, nativeBytes);
                    }

                    // Reinitialize with new configuration
                    inputManager.Reinitialize(yamlContent, userConfigUri);
                    CycloneGames.Logger.CLogger.LogInfo($"[InputSystemInitializer] Configuration updated successfully. Saved: {saveToUserConfig}");
                }
                catch (System.Exception e)
                {
                    CycloneGames.Logger.CLogger.LogError($"[InputSystemInitializer] Failed to update configuration: {e.Message}");
                }
            }
            else
            {
                // First-time initialization
                inputManager.Initialize(yamlContent, userConfigUri);
                CycloneGames.Logger.CLogger.LogInfo("[InputSystemInitializer] Configuration initialized successfully.");

                if (saveToUserConfig)
                {
                    await inputManager.SaveUserConfigurationAsync();
                    CycloneGames.Logger.CLogger.LogInfo($"[InputSystemInitializer] Configuration saved to user config: {userConfigFileName}");
                }
            }
        }

        public async UniTask ReloadUserConfigurationAsync()
        {
            string userConfigFileName = _userConfigFileName ?? "user_input_settings.yaml";
            string userConfigUri = FilePathUtility.GetUnityWebRequestUri(userConfigFileName, UnityPathSource.PersistentData);

            var inputManager = CycloneGames.InputSystem.Runtime.InputManager.Instance;
            if (inputManager == null)
            {
                CycloneGames.Logger.CLogger.LogError("[InputSystemInitializer] Cannot reload user config: InputManager is null.");
                return;
            }

            // Use InputManager's ReloadConfigurationAsync which reads from user config file
            bool success = await inputManager.ReloadConfigurationAsync();
            if (success)
            {
                CycloneGames.Logger.CLogger.LogInfo($"[InputSystemInitializer] User configuration reloaded successfully from: {userConfigFileName}");
            }
            else
            {
                CycloneGames.Logger.CLogger.LogWarning($"[InputSystemInitializer] Failed to reload user configuration from: {userConfigFileName}");
            }
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
                    else
                    {
                        if (!uwr.error.ToLower().Contains("not found"))
                        {
                            CycloneGames.Logger.CLogger.LogWarning($"[InputSystemInitializer] Failed to load from '{uri}': {uwr.error}");
                        }
                        return (false, null);
                    }
                }
                catch (System.Exception e)
                {
                    CycloneGames.Logger.CLogger.LogError($"[InputSystemInitializer] Exception loading from '{uri}': {e.Message}");
                    return (false, null);
                }
            }
        }
    }

    internal class InputPlayerResolver : IInputPlayerResolver
    {
        private readonly InputManager _inputManager;

        public InputPlayerResolver(InputManager inputManager)
        {
            _inputManager = inputManager;
        }

        public IInputPlayer GetInputPlayer(int playerId)
        {
            if (!TryGetInputPlayer(playerId, out var inputPlayer))
            {
                throw new InvalidOperationException($"Failed to get player input for player {playerId}. Ensure InputManager is initialized and player slot exists.");
            }
            return inputPlayer;
        }

        public bool TryGetInputPlayer(int playerId, out IInputPlayer inputPlayer)
        {
            inputPlayer = null;

            if (_inputManager == null)
            {
                return false;
            }

            inputPlayer = _inputManager.GetInputPlayer(playerId);
            if (inputPlayer != null)
            {
                return true;
            }

            inputPlayer = _inputManager.JoinSinglePlayer(playerId);
            return inputPlayer != null;
        }
    }
}
#endif