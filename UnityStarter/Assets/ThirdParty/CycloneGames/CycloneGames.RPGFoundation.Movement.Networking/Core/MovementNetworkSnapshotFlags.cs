namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementNetworkSnapshotFlags
    {
        public const byte None = 0;
        public const byte Grounded = 1 << 0;
        public const byte Teleport = 1 << 1;
        public const byte Predicted = 1 << 2;
        public const byte Reconciled = 1 << 3;
        public const byte AuthorityChanged = 1 << 4;
        public const byte ExternalImpulse = 1 << 5;

        public static bool Has(byte flags, byte flag)
        {
            return (flags & flag) == flag;
        }

        public static byte Set(byte flags, byte flag, bool enabled)
        {
            return enabled
                ? (byte)(flags | flag)
                : (byte)(flags & ~flag);
        }
    }
}
