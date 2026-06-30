namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public struct ProjectileDespawnMessage
    {
        public ulong ProjectileEntityId;
        public int ServerTick;
        public ushort Sequence;
        public int Reason;

        public ProjectileDespawnMessage(
            ulong projectileEntityId,
            int serverTick,
            ushort sequence,
            int reason)
        {
            ProjectileEntityId = projectileEntityId;
            ServerTick = serverTick;
            Sequence = sequence;
            Reason = reason;
        }

        public bool IsValid
        {
            get
            {
                return ProjectileEntityId != 0UL && ServerTick >= 0;
            }
        }
    }
}
