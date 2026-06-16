using System;
using System.Threading;

namespace CycloneGames.Networking.Services
{
    /// <summary>
    /// Central static registry for accessing the active NetworkManager.
    /// Designed to support both Dependency Injection (by ignoring this) and Service Locator patterns.
    /// </summary>
    public static class NetServices
    {
        private static volatile INetworkManager _instance;

        /// <summary>
        /// Access the active NetworkManager.
        /// Throws an exception if no manager has been registered.
        /// </summary>
        public static INetworkManager Instance
        {
            get
            {
                var inst = _instance;
                if (inst == null)
                {
                    throw new InvalidOperationException(
                        "NetServices.Instance is null! Ensure a NetworkAdapter is present in the scene or has registered itself.");
                }
                return inst;
            }
        }

        /// <summary>
        /// Checks if a network manager is currently registered.
        /// </summary>
        public static bool IsAvailable => _instance != null;

        public static bool TryGet(out INetworkManager manager)
        {
            manager = _instance;
            return manager != null;
        }

        /// <summary>
        /// Registers the network manager implementation.
        /// Usually called by the Adapter's Awake method.
        /// </summary>
        public static void Register(INetworkManager manager)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            Interlocked.Exchange(ref _instance, manager);
        }

        /// <summary>
        /// Clears the registration.
        /// Usually called by the Adapter's OnDestroy method.
        /// </summary>
        public static void Unregister(INetworkManager manager)
        {
            if (manager == null)
                return;

            Interlocked.CompareExchange(ref _instance, null, manager);
        }

        public static void Reset()
        {
            Interlocked.Exchange(ref _instance, null);
        }
    }
}
