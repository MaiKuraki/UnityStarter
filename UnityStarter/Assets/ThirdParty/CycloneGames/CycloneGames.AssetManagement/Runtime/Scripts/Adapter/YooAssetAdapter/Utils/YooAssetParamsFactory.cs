#if YOOASSET_PRESENT
using System;
using UnityEngine;
using YooAsset;

namespace CycloneGames.AssetManagement.Runtime
{
    public static class YooAssetParamsFactory
    {
        /// <summary>
        /// Creates the appropriate YooAsset initialization parameters based on the PlayMode and current Platform.
        /// Automatically handles WebGL specifics and configures default file systems.
        /// </summary>
        /// <param name="playMode">The desired play mode.</param>
        /// <param name="defaultHostServer">The default host server URL (for Host/Web modes).</param>
        /// <param name="fallbackHostServer">The fallback host server URL (for Host/Web modes).</param>
        /// <param name="decryptionServices">Optional decryption services (IDecryptionServices or IWebDecryptionServices).</param>
        /// <returns>A YooAsset InitializeParameters object.</returns>
        public static InitializeParameters CreateParameters(
            AssetPlayMode playMode, 
            string defaultHostServer = null, 
            string fallbackHostServer = null,
            object decryptionServices = null)
        {
            InitializeParameters parameters = null;
            IRemoteServices remoteServices = null;
            
            if (!string.IsNullOrEmpty(defaultHostServer))
            {
                remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
            }

            // Cast services
            var standardDecryption = decryptionServices as IDecryptionServices;
            var webDecryption = decryptionServices as IWebDecryptionServices;

#if UNITY_EDITOR
            // In Editor, we respect the requested mode (Simulate, Offline, Host).
            switch (playMode)
            {
                case AssetPlayMode.EditorSimulate:
                    var createParameters = new EditorSimulateModeParameters();
                    string simulateManifestPath = string.Empty;
                    string packageRoot = System.IO.Path.Combine(Environment.CurrentDirectory, "Bundles", "Simulate");
                    createParameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
                    
                    parameters = createParameters;
                    break;
                case AssetPlayMode.Offline:
                    var offlineParams = new OfflinePlayModeParameters();
                    offlineParams.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(standardDecryption);
                    parameters = offlineParams;
                    break;
                case AssetPlayMode.Host:
                    var hostParams = new HostPlayModeParameters();
                    hostParams.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(standardDecryption);
                    hostParams.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, standardDecryption);
                    parameters = hostParams;
                    break;
                case AssetPlayMode.Web:
                    // WebGL in Editor? Use WebPlayMode parameters.
                    var webParams = new WebPlayModeParameters();
                    webParams.WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(webDecryption);
                    webParams.WebRemoteFileSystemParameters = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(remoteServices, webDecryption);
                    parameters = webParams;
                    break;
            }
#else
            // Runtime Platform Logic
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                // WebGL MUST use WebPlayMode (or Offline).
                if (playMode == AssetPlayMode.Offline)
                {
                    var offlineParams = new OfflinePlayModeParameters();
                    offlineParams.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(standardDecryption);
                    parameters = offlineParams;
                }
                else
                {
                    var webParams = new WebPlayModeParameters();
                    webParams.WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(webDecryption);
                    webParams.WebRemoteFileSystemParameters = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(remoteServices, webDecryption);
                    parameters = webParams;
                }
            }
            else
            {
                // Mobile / PC / Console
                switch (playMode)
                {
                    case AssetPlayMode.Offline:
                        var offlineParams = new OfflinePlayModeParameters();
                        offlineParams.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(standardDecryption);
                        parameters = offlineParams;
                        break;
                    case AssetPlayMode.Host:
                    case AssetPlayMode.Web: 
                        var hostParams = new HostPlayModeParameters();
                        hostParams.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(standardDecryption);
                        hostParams.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, standardDecryption);
                        parameters = hostParams;
                        break;
                }
            }
#endif

            if (parameters == null)
            {
                Debug.LogWarning($"[YooAssetParamsFactory] Unsupported PlayMode {playMode} for platform {Application.platform}. Defaulting to Offline.");
                var offlineParams = new OfflinePlayModeParameters();
                offlineParams.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(standardDecryption);
                parameters = offlineParams;
            }

            return parameters;
        }

        // Internal default services classes
        private class RemoteServices : IRemoteServices
        {
            private readonly string _defaultHost, _fallbackHost;
            public RemoteServices(string defaultHost, string fallbackHost)
            {
                _defaultHost = defaultHost;
                _fallbackHost = fallbackHost;
            }
            public string GetRemoteMainURL(string fileName) => $"{_defaultHost}/{fileName}";
            public string GetRemoteFallbackURL(string fileName) => $"{_fallbackHost}/{fileName}";
        }
    }
}
#endif