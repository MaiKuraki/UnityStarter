namespace CycloneGames.Networking
{
    public static class NetworkConstants
    {
        public const int DefaultMTU = 1200;
        public const int MaxMTU = 65535;
        public const int DefaultTickRate = 30;          // Ticks per second
        public const int MinTickRate = 1;
        public const int MaxTickRate = 128;
        public const int DefaultSendRate = 20;          // Snapshots per second
        public const int DefaultMaxConnections = 100;
        public const int DefaultMaxPayloadSize = 1200;
        public const int MaxMessageId = 65535;

        // Reserved message ID ranges. Optional packages claim sub-ranges inside the module-owned range.
        public const ushort SystemMsgIdMin = 0;
        public const ushort SystemMsgIdMax = 999;
        public const ushort RpcMsgIdMin = 1000;
        public const ushort RpcMsgIdMax = 9999;
        public const ushort ModuleMsgIdMin = 10000;
        public const ushort ModuleMsgIdMax = 29999;
        public const ushort UserMsgIdMin = 30000;

        // Timing
        public const float DefaultTimeoutSeconds = 30f;
        public const float DefaultHeartbeatInterval = 5f;
        public const float DefaultDisconnectTimeout = 10f;
        public const double DefaultReconnectWindowSeconds = 300d;
        public const double DefaultHostMigrationTimeoutSeconds = 8d;
        public const int DefaultSessionSearchMaxResults = 50;

        // Buffer sizes
        public const int DefaultBufferSize = 1500;
        public const int DefaultPoolSize = 32;
        public const int MaxSnapshotBufferSize = 64;
    }
}
