namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public struct ProjectileFullStateRequestMessage
    {
        public ulong ProjectileEntityId;
        public int ClientTick;
        public int LastReceivedServerTick;
        public ushort Sequence;

        public ProjectileFullStateRequestMessage(
            ulong projectileEntityId,
            int clientTick,
            int lastReceivedServerTick,
            ushort sequence)
        {
            ProjectileEntityId = projectileEntityId;
            ClientTick = clientTick;
            LastReceivedServerTick = lastReceivedServerTick;
            Sequence = sequence;
        }

        public bool IsValid
        {
            get
            {
                return ProjectileEntityId != 0UL
                       && ClientTick >= 0
                       && LastReceivedServerTick >= 0;
            }
        }
    }
}
