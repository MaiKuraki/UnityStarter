using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Central static registry for accessing the active NetworkManager.
    /// Designed to support both Dependency Injection (by ignoring this) and Service Locator patterns.
    /// </summary>
    public static class NetServices
    {
        private static INetworkManager _instance;

        /// <summary>
        /// Access the active NetworkManager.
        /// Throws an exception if no manager has been registered.
        /// </summary>
        public static INetworkManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException(
                        "NetServices.Instance is null! Ensure a NetworkAdapter is present in the scene or has registered itself.");
                }
                return _instance;
            }
        }

        /// <summary>
        /// Checks if a network manager is currently registered.
        /// </summary>
        public static bool IsAvailable => _instance != null;

        /// <summary>
        /// Registers the network manager implementation.
        /// Usually called by the Adapter's Awake method.
        /// </summary>
        public static void Register(INetworkManager manager)
        {
            _instance = manager;
        }

        /// <summary>
        /// Clears the registration.
        /// Usually called by the Adapter's OnDestroy method.
        /// </summary>
        public static void Unregister(INetworkManager manager)
        {
            if (_instance == manager)
            {
                _instance = null;
            }
        }
    }
}