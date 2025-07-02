using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// A pure C# static class responsible for loading the input configuration
    /// and initializing the InputManager. It now supports a primary user path
    /// and a fallback default path to enable user-specific settings.
    /// </summary>
    public static class InputSystemLoader
    {
        /// <summary>
        /// Asynchronously loads the configuration and initializes the InputManager.
        /// It first tries to load from the userConfigUri. If that fails or the file
        /// doesn't exist, it loads from the defaultConfigUri and then saves a copy
        /// to the userConfigUri location.
        /// </summary>
        /// <param name="defaultConfigUri">The URI for the default, read-only configuration (e.g., from StreamingAssets).</param>
        /// <param name="userConfigUri">The URI for the user-specific, writable configuration (e.g., in PersistentDataPath).</param>
        public static async Task InitializeAsync(string defaultConfigUri, string userConfigUri)
        {
            string yamlContent = null;
            bool loadedFromUserConfig = false;

            // 1. Try to load from the user-specific configuration path first.
            if (!string.IsNullOrEmpty(userConfigUri))
            {
                (bool success, string content) = await LoadConfigFromUriAsync(userConfigUri);
                if (success)
                {
                    yamlContent = content;
                    loadedFromUserConfig = true;
                    Debug.Log($"[InputSystemLoader] Successfully loaded user config from: {userConfigUri}");
                }
            }

            // 2. If user config failed to load, fall back to the default configuration.
            if (string.IsNullOrEmpty(yamlContent))
            {
                if (string.IsNullOrEmpty(defaultConfigUri))
                {
                    Debug.LogError("[InputSystemLoader] Both user and default config URIs are invalid. Initialization failed.");
                    return;
                }
                
                (bool success, string content) = await LoadConfigFromUriAsync(defaultConfigUri);
                if (success)
                {
                    yamlContent = content;
                    Debug.Log($"[InputSystemLoader] Loaded default config from: {defaultConfigUri}. Will create user copy.");
                }
                else
                {
                    Debug.LogError($"[InputSystemLoader] CRITICAL: Failed to load both user and default configurations. Input system will not function.");
                    return;
                }
            }

            // 3. Initialize the manager with the loaded content.
            if (!string.IsNullOrEmpty(yamlContent))
            {
                InputManager.Instance.Initialize(yamlContent, userConfigUri);

                // 4. If we loaded from the default config, save it as the initial user config.
                if (!loadedFromUserConfig)
                {
                    await InputManager.Instance.SaveUserConfigurationAsync();
                }
            }
        }

        /// <summary>
        /// Helper method to perform a UnityWebRequest to get text content from a URI.
        /// </summary>
        /// <returns>A tuple containing success status and file content.</returns>
        private static async Task<(bool, string)> LoadConfigFromUriAsync(string uri)
        {
            using (UnityWebRequest uwr = UnityWebRequest.Get(uri))
            {
                try
                {
                    var asyncOperation = uwr.SendWebRequest();
                    while (!asyncOperation.isDone)
                    {
                        await Task.Yield();
                    }

                    // For file paths, "Not Found" is a common, valid case (e.g., user runs for the first time).
                    // We treat it as a non-successful load but not a critical error.
                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        return (true, uwr.downloadHandler.text);
                    }
                    else
                    {
                        // Log other types of errors (e.g., malformed URI, connection issues).
                        // Don't log "Not Found" as an error since it's an expected fallback condition.
                        if(!uwr.error.ToLower().Contains("not found"))
                        {
                            Debug.LogWarning($"[InputSystemLoader] Failed to load from '{uri}': {uwr.error}");
                        }
                        return (false, null);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[InputSystemLoader] An exception occurred while loading from '{uri}': {e.Message}");
                    return (false, null);
                }
            }
        }
    }
}