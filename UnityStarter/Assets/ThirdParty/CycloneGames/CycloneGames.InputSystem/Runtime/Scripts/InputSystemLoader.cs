using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;
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
        public static UniTask InitializeAsync(string defaultConfigUri, string userConfigUri)
        {
            return InitializeAsync(defaultConfigUri, userConfigUri, default);
        }

        /// <summary>
        /// Loads config from userConfigUri, falls back to defaultConfigUri if not found.
        /// </summary>
        public static UniTask InitializeAsync(string defaultConfigUri, string userConfigUri, CancellationToken cancellationToken)
        {
            return InitializeInternalAsync(defaultConfigUri, userConfigUri, cancellationToken, false);
        }

        private static async UniTask InitializeInternalAsync(
            string defaultConfigUri,
            string userConfigUri,
            CancellationToken cancellationToken,
            bool forceReinitialize)
        {
            string yamlContent = null;
            string defaultYamlContent = null;
            bool loadedFromUserConfig = false;
            bool userConfigCorrupted = false;

            // Always load default config first for fallback
            if (!string.IsNullOrEmpty(defaultConfigUri))
            {
                (bool success, string content) = await InputConfigurationFileLoader.LoadTextFromUriAsync(defaultConfigUri, DEBUG_FLAG, cancellationToken);
                if (success)
                {
                    defaultYamlContent = content;
                    CLogger.LogInfo($"{DEBUG_FLAG} Loaded default config from: {defaultConfigUri}");
                }
            }

            // Try loading user config
            if (!string.IsNullOrEmpty(userConfigUri))
            {
                (bool success, string content) = await InputConfigurationFileLoader.LoadTextFromUriAsync(userConfigUri, DEBUG_FLAG, cancellationToken);
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
                        await InputConfigurationFileLoader.DeleteTextAtUriAsync(userConfigUri, DEBUG_FLAG, cancellationToken);
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
                if (forceReinitialize)
                {
                    InputManager.Instance.Reinitialize(yamlContent, userConfigUri);
                }
                else
                {
                    InputManager.Instance.Initialize(yamlContent, userConfigUri);
                }
                 
                // Save user config if: not loaded from user config OR user config was corrupted
                if (!loadedFromUserConfig || userConfigCorrupted)
                {
                    await InputManager.Instance.SaveUserConfigurationAsync(cancellationToken);
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
                if (config == null || config.PlayerSlots == null) return false;

#if UNITY_EDITOR
                // Schema fingerprint check: editor-only, not needed in shipped builds
                if (config.SchemaFingerprint != InputSchemaFingerprint.Current)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Schema fingerprint mismatch: file has [{config.SchemaFingerprint ?? "none"}], current is [{InputSchemaFingerprint.Current}]. " +
                                       "Some settings may use default values. Re-save in editor to upgrade.");
                }
#endif

                return true;
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
        public static UniTask<bool> ResetToDefaultAsync(string defaultConfigUri, string userConfigUri)
        {
            return ResetToDefaultAsync(defaultConfigUri, userConfigUri, default);
        }

        /// <summary>
        /// Resets user configuration to default. Deletes user config file and reinitializes with default config.
        /// Cross-platform compatible: Windows, macOS, Linux, Android, iOS, WebGL.
        /// </summary>
        /// <param name="defaultConfigUri">URI to default config (e.g., StreamingAssets)</param>
        /// <param name="userConfigUri">URI to user config (e.g., PersistentData)</param>
        /// <param name="cancellationToken">Cancellation token for file and UnityWebRequest loading.</param>
        /// <returns>True if reset was successful</returns>
        public static async UniTask<bool> ResetToDefaultAsync(string defaultConfigUri, string userConfigUri, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(defaultConfigUri))
            {
                CLogger.LogError($"{DEBUG_FLAG} Cannot reset: defaultConfigUri is null or empty.");
                return false;
            }

            bool deleteSuccess = await InputConfigurationFileLoader.DeleteTextAtUriAsync(userConfigUri, DEBUG_FLAG, cancellationToken);
            if (!deleteSuccess)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Failed to delete user config, but will continue with reset.");
            }

            await InitializeInternalAsync(defaultConfigUri, userConfigUri, cancellationToken, true);
            
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
            return InputConfigurationFileLoader.TryDeleteTextAtUri(userConfigUri, DEBUG_FLAG);
        }
    }

    /// <summary>
    /// Centralized text configuration storage for local files, WebGL player storage, and UnityWebRequest-only locations.
    /// </summary>
    public static class InputConfigurationFileLoader
    {
        public static async UniTask<(bool Success, string Content)> LoadTextFromUriAsync(
            string uri,
            string logPrefix,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return (false, null);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            string playerPrefsKey = GetPlayerPrefsKeyFromUri(uri);
            await SwitchToUnityMainThreadAsync(cancellationToken);
            if (PlayerPrefs.HasKey(playerPrefsKey))
            {
                return (true, PlayerPrefs.GetString(playerPrefsKey));
            }

            if (LooksLikeLocalFileUri(uri))
            {
                return (false, null);
            }
#endif

            if (TryGetLocalFilePath(uri, out string filePath))
            {
                return await LoadTextFromLocalFileAsync(filePath, logPrefix, cancellationToken);
            }

            return await LoadTextFromUnityWebRequestAsync(uri, logPrefix, cancellationToken);
        }

        public static async UniTask<bool> SaveTextToUriAsync(
            string uri,
            string content,
            string logPrefix,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return false;
            }

            content ??= string.Empty;

#if UNITY_WEBGL && !UNITY_EDITOR
            await SwitchToUnityMainThreadAsync(cancellationToken);
            string playerPrefsKey = GetPlayerPrefsKeyFromUri(uri);
            PlayerPrefs.SetString(playerPrefsKey, content);
            PlayerPrefs.Save();
            CLogger.LogInfo($"{logPrefix} Saved text config to PlayerPrefs: {playerPrefsKey}");
            return true;
#else
            if (!TryGetLocalFilePath(uri, out string filePath))
            {
                CLogger.LogWarning($"{logPrefix} Cannot save config because URI is not a local writable file: {uri}");
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await WriteAllTextToLocalFileAsync(filePath, content, cancellationToken);
                CLogger.LogInfo($"{logPrefix} Saved text config to: {filePath}");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                CLogger.LogError($"{logPrefix} Failed to save config to '{filePath}': {e.Message}");
                return false;
            }
#endif
        }

        public static UniTask<bool> DeleteTextAtUriAsync(
            string uri,
            string logPrefix,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return UniTask.FromResult(true);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return DeleteTextAtUriWebGLAsync(uri, logPrefix, cancellationToken);
#else
            return UniTask.FromResult(TryDeleteTextAtUri(uri, logPrefix));
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static async UniTask<bool> DeleteTextAtUriWebGLAsync(
            string uri,
            string logPrefix,
            CancellationToken cancellationToken)
        {
            await SwitchToUnityMainThreadAsync(cancellationToken);
            string playerPrefsKey = GetPlayerPrefsKeyFromUri(uri);
            if (PlayerPrefs.HasKey(playerPrefsKey))
            {
                PlayerPrefs.DeleteKey(playerPrefsKey);
                PlayerPrefs.Save();
                CLogger.LogInfo($"{logPrefix} Deleted text config from PlayerPrefs: {playerPrefsKey}");
            }

            return true;
        }
#endif

        public static bool TryDeleteTextAtUri(string uri, string logPrefix)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return true;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
            {
                CLogger.LogWarning($"{logPrefix} Cannot delete WebGL PlayerPrefs config from a non-main thread. Use DeleteTextAtUriAsync instead.");
                return false;
            }

            string playerPrefsKey = GetPlayerPrefsKeyFromUri(uri);
            if (PlayerPrefs.HasKey(playerPrefsKey))
            {
                PlayerPrefs.DeleteKey(playerPrefsKey);
                PlayerPrefs.Save();
                CLogger.LogInfo($"{logPrefix} Deleted text config from PlayerPrefs: {playerPrefsKey}");
            }

            return true;
#else
            try
            {
                if (!TryGetLocalFilePath(uri, out string filePath))
                {
                    return false;
                }

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    CLogger.LogInfo($"{logPrefix} Deleted text config file: {filePath}");
                }

                return true;
            }
            catch (Exception e)
            {
                CLogger.LogWarning($"{logPrefix} Failed to delete config '{uri}': {e.Message}");
                return false;
            }
#endif
        }

        public static bool TryGetLocalFilePath(string uri, out string filePath)
        {
            filePath = null;

            if (string.IsNullOrEmpty(uri) ||
                uri.StartsWith("jar:file://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            try
            {
                if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var parsedUri = new Uri(uri);
                    if (!parsedUri.IsFile)
                    {
                        return false;
                    }

                    filePath = parsedUri.LocalPath;
                    return !string.IsNullOrEmpty(filePath);
                }

                if (Path.IsPathRooted(uri))
                {
                    filePath = uri;
                    return true;
                }

                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri absoluteUri) && absoluteUri.IsFile)
                {
                    filePath = absoluteUri.LocalPath;
                    return !string.IsNullOrEmpty(filePath);
                }
            }
            catch (Exception e)
            {
                CLogger.LogWarning($"[InputConfigurationFileLoader] Failed to parse URI '{uri}': {e.Message}");
            }

            return false;
#endif
        }

        private static async UniTask<(bool Success, string Content)> LoadTextFromLocalFileAsync(
            string filePath,
            string logPrefix,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return (false, null);
                }

                string content = await ReadAllTextFromLocalFileAsync(filePath, cancellationToken);
                return (true, content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                CLogger.LogWarning($"{logPrefix} Failed to load local config from '{filePath}': {e.Message}");
                return (false, null);
            }
        }

        private static async UniTask<string> ReadAllTextFromLocalFileAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            using (var stream = new FileStream(
                       filePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                long length = stream.Length;
                if (length == 0L)
                {
                    return string.Empty;
                }

                if (length > int.MaxValue)
                {
                    throw new IOException($"Input config file is too large: {length} bytes.");
                }

                byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent((int)length);
                try
                {
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        int read = await stream.ReadAsync(rentedBuffer, totalRead, (int)length - totalRead, cancellationToken);
                        if (read == 0)
                        {
                            break;
                        }

                        totalRead += read;
                    }

                    return Encoding.UTF8.GetString(rentedBuffer, 0, totalRead);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
        }

        private static async UniTask WriteAllTextToLocalFileAsync(
            string filePath,
            string content,
            CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            using (var stream = new FileStream(
                       filePath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            }
        }

        private static async UniTask<(bool Success, string Content)> LoadTextFromUnityWebRequestAsync(
            string uri,
            string logPrefix,
            CancellationToken cancellationToken)
        {
            try
            {
                await SwitchToUnityMainThreadAsync(cancellationToken);

                using (UnityWebRequest uwr = UnityWebRequest.Get(uri))
                {
                    var asyncOperation = uwr.SendWebRequest();
                    while (!asyncOperation.isDone)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    }

                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        return (true, uwr.downloadHandler.text);
                    }

                    if (!IsNotFoundError(uwr.error))
                    {
                        CLogger.LogWarning($"{logPrefix} Failed to load from '{uri}': {uwr.error}");
                    }

                    return (false, null);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                CLogger.LogError($"{logPrefix} Exception loading from '{uri}': {e.Message}");
                return (false, null);
            }
        }

        private static async UniTask SwitchToUnityMainThreadAsync(CancellationToken cancellationToken)
        {
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private static bool IsNotFoundError(string error)
        {
            return error != null &&
                   (error.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static string GetPlayerPrefsKeyFromUri(string uri)
        {
            return $"InputConfig_{InputHashUtility.GetDeterministicHashCode(uri):X8}";
        }

        private static bool LooksLikeLocalFileUri(string uri)
        {
            return uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(uri);
        }
#endif
    }
}
