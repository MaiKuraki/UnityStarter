using System.Threading.Tasks;
using CycloneGames.Logger;
using UnityEngine.Networking;

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
            bool loadedFromUserConfig = false;

            if (!string.IsNullOrEmpty(userConfigUri))
            {
                (bool success, string content) = await LoadConfigFromUriAsync(userConfigUri);
                if (success)
                {
                    yamlContent = content;
                    loadedFromUserConfig = true;
                    CLogger.LogInfo($"{DEBUG_FLAG} Loaded user config from: {userConfigUri}");
                }
            }

            if (string.IsNullOrEmpty(yamlContent))
            {
                if (string.IsNullOrEmpty(defaultConfigUri))
                {
                    CLogger.LogError($"{DEBUG_FLAG} Both config URIs invalid. Initialization failed.");
                    return;
                }

                (bool success, string content) = await LoadConfigFromUriAsync(defaultConfigUri);
                if (success)
                {
                    yamlContent = content;
                    CLogger.LogInfo($"{DEBUG_FLAG} Loaded default config from: {defaultConfigUri}");
                }
                else
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to load both configurations.");
                    return;
                }
            }

            if (!string.IsNullOrEmpty(yamlContent))
            {
                InputManager.Instance.Initialize(yamlContent, userConfigUri);
                if (!loadedFromUserConfig)
                {
                    await InputManager.Instance.SaveUserConfigurationAsync();
                }
            }
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