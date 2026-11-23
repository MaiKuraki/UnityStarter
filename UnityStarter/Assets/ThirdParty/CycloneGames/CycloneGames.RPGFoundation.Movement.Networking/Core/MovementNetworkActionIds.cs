namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementWireIds
    {
        public const ushort MessageIdBase = 16000;
        public const ushort MessageIdMax = 16999;

        public const ushort ManifestHandshake = MessageIdBase;
        public const ushort InputCommand = MessageIdBase + 1;
        public const ushort AuthoritativeSnapshot = MessageIdBase + 2;
        public const ushort Correction = MessageIdBase + 3;
        public const ushort FullStateRequest = MessageIdBase + 4;
        public const ushort AuthorityTransfer = MessageIdBase + 5;
        public const ushort Teleport = MessageIdBase + 6;
        public const ushort InputBatch = MessageIdBase + 7;
        public const ushort InputAck = MessageIdBase + 8;

        public const ushort FirstBuiltIn = ManifestHandshake;
        public const ushort LastBuiltIn = InputAck;
        public const int BuiltInMessageCount = 9;

        public const uint InputCommandAction = 1U;
        public const uint AuthoritativeSnapshotAction = 2U;
        public const uint CorrectionAction = 3U;
        public const uint TeleportAction = 4U;
        public const uint AuthorityTransferAction = 5U;
        public const uint InputBatchAction = 6U;
        public const uint InputAckAction = 7U;

        public const ushort MaximumStateId = 10;
        public const byte MaximumJumpCount = 32;
        public const ulong InvalidEpoch = 0UL;
        public const ulong InvalidFingerprint = 0UL;

        public static bool IsBuiltInMessage(ushort messageId)
        {
            return messageId >= FirstBuiltIn && messageId <= LastBuiltIn;
        }
    }

    public static class MovementWireLimits
    {
        public static readonly int MaxInputPayloadSize = 128;
        public static readonly int MaxSnapshotPayloadSize = 256;
        public static readonly int MaxCorrectionPayloadSize = 1024;
        public static readonly int MaxControlPayloadSize = 128;
        public static readonly int MaxInputBatchPayloadSize = 1024;
        public static readonly int MaxInputAckPayloadSize = 128;
        public static readonly float MaxInputDeltaTime = 1f;
        public static readonly int MaxInputBatchCount = 8;
        public static readonly double WorldUpUnitTolerance = 0.05;
    }

    public readonly struct MovementHistoryCapacity
    {
        public readonly int EntityCount;
        public readonly int PerEntityCount;

        public MovementHistoryCapacity(int entityCount, int perEntityCount)
        {
            if (entityCount <= 0 || perEntityCount <= 0)
                throw new System.ArgumentOutOfRangeException("MovementHistoryCapacity requires positive capacities.");
            EntityCount = entityCount;
            PerEntityCount = perEntityCount;
        }

        public static MovementHistoryCapacity Default
        {
            get { return new MovementHistoryCapacity(128, 128); }
        }
    }

    public readonly struct MovementDilationPolicy
    {
        public readonly int TickRate;
        public readonly int WindowTicks;
        public readonly double MaxDilationRatio;
        public readonly int MaxEntities;
        public readonly double BurstAllowanceSeconds;
        public readonly int SweepInterval;

        public MovementDilationPolicy(
            int tickRate = 60,
            int windowTicks = 120,
            double maxDilationRatio = 1.25,
            int maxEntities = 1024,
            double burstAllowanceSeconds = 0.05,
            int sweepInterval = 1024)
        {
            if (tickRate <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(tickRate));
            if (windowTicks <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(windowTicks));
            if (double.IsNaN(maxDilationRatio) || double.IsInfinity(maxDilationRatio) || maxDilationRatio < 1.0)
                throw new System.ArgumentOutOfRangeException(nameof(maxDilationRatio));
            if (maxEntities <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxEntities));
            if (double.IsNaN(burstAllowanceSeconds) || double.IsInfinity(burstAllowanceSeconds) || burstAllowanceSeconds < 0.0)
                throw new System.ArgumentOutOfRangeException(nameof(burstAllowanceSeconds));
            if (sweepInterval <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(sweepInterval));

            TickRate = tickRate;
            WindowTicks = windowTicks;
            MaxDilationRatio = maxDilationRatio;
            MaxEntities = maxEntities;
            BurstAllowanceSeconds = burstAllowanceSeconds;
            SweepInterval = sweepInterval;
        }

        public static MovementDilationPolicy Default
        {
            get { return new MovementDilationPolicy(60, 120, 1.25, 1024, 0.05, 1024); }
        }
    }
}
