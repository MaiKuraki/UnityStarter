using System;
using System.IO;
using System.Threading.Tasks;
using CycloneGames.Logger;
using UnityEngine.Networking;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Loads input configuration and initializes InputManager. Supports user config with default fallback.
    /// </summary>
    public static class InputSystemLoader
    {
        private const string DEBUG_FLAG = "[InputSystemLoader]";

        /// <summary>
        /// Loads config from userConfigUri, falls back to defaultConfigUri if not found.
        /// </summary>
        public static async Task InitializeAsync(string defaultConfigUri, string userConfigUri)
        {
            string yamlContent = null;
            string defaultYamlContent = null;
            bool loadedFromUserConfig = false;
            bool userConfigCorrupted = false;

            // Always load default config first for fallback
            if (!string.IsNullOrEmpty(defaultConfigUri))
            {
                (bool success, string content) = await LoadConfigFromUriAsync(defaultConfigUri);
                if (success)
                {
                    defaultYamlContent = content;
                    CLogger.LogInfo($"{DEBUG_FLAG} Loaded default config from: {defaultConfigUri}");
                }
            }

            // Try loading user config
            if (!string.IsNullOrEmpty(userConfigUri))
            {
                (bool success, string content) = await LoadConfigFromUriAsync(userConfigUri);
                if (success && !string.IsNullOrEmpty(content))
                {
                    // Validate user config before use
                    if (ValidateYamlContent(content))
                    {
                        yamlContent = content;
                        loadedFromUserConfig = true;
                        CLogger.LogInfo($"{DEBUG_FLAG} Loaded and validated user config from: {userConfigUri}");
                    }
                    else
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} User config is corrupted or invalid, will use default config. Uri: {userConfigUri}");
                        userConfigCorrupted = true;
                        
                        // Delete corrupted user config file
                        TryDeleteCorruptedUserConfig(userConfigUri);
                    }
                }
            }

            // Fallback to default config
            if (string.IsNullOrEmpty(yamlContent))
            {
                if (string.IsNullOrEmpty(defaultYamlContent))
                {
                    CLogger.LogError($"{DEBUG_FLAG} Both config URIs invalid. Initialization failed.");
                    return;
                }
                yamlContent = defaultYamlContent;
            }

            if (!string.IsNullOrEmpty(yamlContent))
            {
                InputManager.Instance.Initialize(yamlContent, userConfigUri);
                
                // Save user config if: not loaded from user config OR user config was corrupted
                if (!loadedFromUserConfig || userConfigCorrupted)
                {
                    await InputManager.Instance.SaveUserConfigurationAsync();
                    CLogger.LogInfo($"{DEBUG_FLAG} Saved fresh user config.");
                }
            }
        }

        /// <summary>
        /// Validates YAML content by attempting to parse it.
        /// Returns true if valid, false if corrupted/invalid.
        /// </summary>
        private static bool ValidateYamlContent(string yamlContent)
        {
            if (string.IsNullOrEmpty(yamlContent)) return false;

            try
            {
                // Normalize line endings to handle cross-platform issues (Windows CRLF vs Unix LF)
                string normalizedContent = NormalizeLineEndings(yamlContent);
                
                // Try to parse the YAML
                var config = YamlSerializer.Deserialize<InputConfiguration>(System.Text.Encoding.UTF8.GetBytes(normalizedContent));
                return config != null && config.PlayerSlots != null;
            }
            catch (Exception e)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} YAML validation failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Normalizes line endings to Unix-style (LF only) for cross-platform compatibility.
        /// </summary>
        private static string NormalizeLineEndings(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            // Replace CRLF with LF, then any remaining CR with LF
            return content.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        /// <summary>
        /// Resets user configuration to default. Deletes user config file and reinitializes with default config.
        /// Cross-platform compatible: Windows, macOS, Linux, Android, iOS, WebGL.
        /// </summary>
        /// <param name="defaultConfigUri">URI to default config (e.g., StreamingAssets)</param>
        /// <param name="userConfigUri">URI to user config (e.g., PersistentData)</param>
        /// <returns>True if reset was successful</returns>
        public static async Task<bool> ResetToDefaultAsync(string defaultConfigUri, string userConfigUri)
        {
            if (string.IsNullOrEmpty(defaultConfigUri))
            {
                CLogger.LogError($"{DEBUG_FLAG} Cannot reset: defaultConfigUri is null or empty.");
                return false;
            }

            bool deleteSuccess = TryDeleteUserConfigFile(userConfigUri);
            if (!deleteSuccess)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Failed to delete user config, but will continue with reset.");
            }

            await InitializeAsync(defaultConfigUri, userConfigUri);
            
            CLogger.LogInfo($"{DEBUG_FLAG} Reset to default configuration completed.");
            return true;
        }

        /// <summary>
        /// Deletes user config file. Cross-platform compatible.
        /// </summary>
        /// <param name="userConfigUri">URI to user config file</param>
        /// <returns>True if deletion was successful or file didn't exist</returns>
        public static bool TryDeleteUserConfigFile(string userConfigUri)
        {
            if (string.IsNullOrEmpty(userConfigUri))
            {
                return true; // Nothing to delete
            }

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL: Use PlayerPrefs-based storage or IndexedDB
                // Since we can't directly delete files in WebGL, we mark it for recreation
                string key = GetPlayerPrefsKeyFromUri(userConfigUri);
                if (UnityEngine.PlayerPrefs.HasKey(key))
                {
                    UnityEngine.PlayerPrefs.DeleteKey(key);
                    UnityEngine.PlayerPrefs.Save();
                    CLogger.LogInfo($"{DEBUG_FLAG} Deleted user config from PlayerPrefs: {key}");
                }
                return true;
#else
                // All other platforms: Standard file deletion
                string filePath = GetFilePathFromUri(userConfigUri);
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    File.Delete(filePath);
                    CLogger.LogInfo($"{DEBUG_FLAG} Deleted user config file: {filePath}");
                }
                return true;
#endif
            }
            catch (Exception e)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Failed to delete user config: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extracts file path from URI. Handles different URI formats for cross-platform compatibility.
        /// </summary>
        private static string GetFilePathFromUri(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return null;

            try
            {
                // Handle file:// URIs
                if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    return new Uri(uri).LocalPath;
                }
                
                // Handle direct paths (Android, iOS, etc.)
                if (uri.StartsWith("/") || (uri.Length > 1 && uri[1] == ':'))
                {
                    return uri;
                }

                // Try parsing as URI
                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri parsedUri))
                {
                    return parsedUri.LocalPath;
                }

                return uri;
            }
            catch (Exception e)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Failed to parse URI '{uri}': {e.Message}");
                return null;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// Generates a PlayerPrefs key from URI for WebGL storage.
        /// </summary>
        private static string GetPlayerPrefsKeyFromUri(string uri)
        {
            // Create a stable key from the URI
            return $"InputConfig_{uri.GetHashCode():X8}";
        }
#endif

        /// <summary>
        /// Attempts to delete a corrupted user config file.
        /// </summary>
        private static void TryDeleteCorruptedUserConfig(string userConfigUri)
        {
            TryDeleteUserConfigFile(userConfigUri);
        }

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

                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        return (true, uwr.downloadHandler.text);
                    }
                    else
                    {
                        if (!uwr.error.ToLower().Contains("not found"))
                        {
                            CLogger.LogWarning($"{DEBUG_FLAG} Failed to load from '{uri}': {uwr.error}");
                        }
                        return (false, null);
                    }
                }
                catch (System.Exception e)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Exception loading from '{uri}': {e.Message}");
                    return (false, null);
                }
            }
        }
    }
}