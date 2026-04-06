using System;
using UnityEngine;

namespace CycloneGames.Networking.Platform
{
    /// <summary>
    /// Platform-specific network configuration.
    /// Different platforms have different MTU, transport, and capability constraints.
    /// </summary>
    [Serializable]
    public sealed class NetworkPlatformConfig
    {
        [Header("Transport")]
        public int MaxMTU = NetworkConstants.DefaultMTU;
        public int MaxConnections = NetworkConstants.DefaultMaxConnections;
        public bool SupportsIPv6 = true;
        public bool SupportsUDP = true;
        public bool SupportsTCP = true;
        public bool SupportsWebSocket = false;
        public bool SupportsRelay = false;

        [Header("Timing")]
        public int TickRate = NetworkConstants.DefaultTickRate;
        public int SendRate = NetworkConstants.DefaultSendRate;
        public float TimeoutSeconds = NetworkConstants.DefaultTimeoutSeconds;
        public float HeartbeatInterval = NetworkConstants.DefaultHeartbeatInterval;

        [Header("Bandwidth")]
        public int MaxBytesPerSecondSend = 65536;
        public int MaxBytesPerSecondReceive = 65536;
        public bool EnableCompression = false;

        [Header("Security")]
        public bool RequireEncryption = false;
        public bool RequireAuthentication = true;
        public int MaxMessagesPerSecond = 60;

        [Header("Buffer")]
        public int SendBufferSize = 65536;
        public int ReceiveBufferSize = 65536;
        public int BufferPoolSize = NetworkConstants.DefaultPoolSize;

        /// <summary>
        /// Get recommended config for the current build platform.
        /// </summary>
        public static NetworkPlatformConfig GetForCurrentPlatform()
        {
#if UNITY_WEBGL
            return WebGL();
#elif UNITY_IOS
            return IOS();
#elif UNITY_ANDROID
            return Android();
#elif UNITY_PS4 || UNITY_PS5
            return PlayStation();
#elif UNITY_XBOXONE || UNITY_GAMECORE
            return Xbox();
#elif UNITY_SWITCH
            return NintendoSwitch();
#elif UNITY_STANDALONE_OSX
            return MacOS();
#elif UNITY_STANDALONE_LINUX
            return Linux();
#else
            return Windows();
#endif
        }

        public static NetworkPlatformConfig Windows() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 200,
            SupportsIPv6 = true,
            TickRate = 60,
            SendRate = 30,
            MaxBytesPerSecondSend = 131072,
            MaxBytesPerSecondReceive = 131072,
            SendBufferSize = 131072,
            ReceiveBufferSize = 131072
        };

        public static NetworkPlatformConfig MacOS() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 200,
            SupportsIPv6 = true,
            TickRate = 60,
            SendRate = 30,
            MaxBytesPerSecondSend = 131072
        };

        public static NetworkPlatformConfig Linux() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 500,  // Dedicated server use case
            SupportsIPv6 = true,
            TickRate = 64,
            SendRate = 30,
            MaxBytesPerSecondSend = 262144,
            MaxBytesPerSecondReceive = 262144,
            SendBufferSize = 262144,
            ReceiveBufferSize = 262144
        };

        public static NetworkPlatformConfig WebGL() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 1,  // Client-only
            SupportsIPv6 = false,
            SupportsUDP = false,
            SupportsTCP = false,
            SupportsWebSocket = true,
            TickRate = 30,
            SendRate = 20,
            MaxBytesPerSecondSend = 32768,
            MaxBytesPerSecondReceive = 65536,
            EnableCompression = true,
            MaxMessagesPerSecond = 30,
            SendBufferSize = 32768,
            ReceiveBufferSize = 32768
        };

        public static NetworkPlatformConfig IOS() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 8,
            SupportsIPv6 = true,
            TickRate = 30,
            SendRate = 20,
            MaxBytesPerSecondSend = 32768,
            EnableCompression = true,
            MaxMessagesPerSecond = 40,
            SendBufferSize = 32768,
            ReceiveBufferSize = 65536
        };

        public static NetworkPlatformConfig Android() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 8,
            SupportsIPv6 = true,
            TickRate = 30,
            SendRate = 20,
            MaxBytesPerSecondSend = 32768,
            EnableCompression = true,
            MaxMessagesPerSecond = 40,
            SendBufferSize = 32768,
            ReceiveBufferSize = 65536
        };

        public static NetworkPlatformConfig PlayStation() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 100,
            SupportsIPv6 = true,
            SupportsRelay = true,
            TickRate = 60,
            SendRate = 30,
            RequireEncryption = true,
            RequireAuthentication = true,
            MaxBytesPerSecondSend = 131072
        };

        public static NetworkPlatformConfig Xbox() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 100,
            SupportsIPv6 = true,
            SupportsRelay = true,
            TickRate = 60,
            SendRate = 30,
            RequireEncryption = true,
            RequireAuthentication = true,
            MaxBytesPerSecondSend = 131072
        };

        public static NetworkPlatformConfig NintendoSwitch() => new NetworkPlatformConfig
        {
            MaxMTU = 1200,
            MaxConnections = 16,
            SupportsIPv6 = true,
            TickRate = 30,
            SendRate = 20,
            MaxBytesPerSecondSend = 32768,
            EnableCompression = true,
            MaxMessagesPerSecond = 30,
            SendBufferSize = 32768,
            ReceiveBufferSize = 32768,
            RequireAuthentication = true
        };
    }
}
