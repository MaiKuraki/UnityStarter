namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public struct ProjectileCorrectionMessage
    {
        public ulong ProjectileEntityId;
        public int ServerTick;
        public ushort Sequence;
        public uint CorrectionFlags;
        public ProjectileSnapshotMessage Snapshot;

        public ProjectileCorrectionMessage(
            ulong projectileEntityId,
            int serverTick,
            ushort sequence,
            uint correctionFlags,
            ProjectileSnapshotMessage snapshot)
        {
            ProjectileEntityId = projectileEntityId;
            ServerTick = serverTick;
            Sequence = sequence;
            CorrectionFlags = correctionFlags;
            Snapshot = snapshot;
        }

        public bool IsValid
        {
            get
            {
                return ProjectileEntityId != 0UL
                       && ServerTick >= 0
                       && Snapshot.IsValid
                       && Snapshot.ProjectileEntityId == ProjectileEntityId;
            }
        }
    }
}
