namespace CycloneGames.Networking.Simulation
{
    public readonly struct NetworkActionStateSnapshot
    {
        public readonly ulong EntityId;
        public readonly uint ActionId;
        public readonly NetworkTickId ServerTick;
        public readonly ushort Sequence;
        public readonly NetworkActionPhase Phase;
        public readonly NetworkVector3 Position;
        public readonly NetworkVector3 Velocity;
        public readonly ulong StateHash;
        public readonly uint CustomFlags;

        public NetworkActionStateSnapshot(
            ulong entityId,
            uint actionId,
            NetworkTickId serverTick,
            ushort sequence,
            NetworkActionPhase phase,
            NetworkVector3 position = default,
            NetworkVector3 velocity = default,
            ulong stateHash = 0UL,
            uint customFlags = 0U)
        {
            EntityId = entityId;
            ActionId = actionId;
            ServerTick = serverTick;
            Sequence = sequence;
            Phase = phase;
            Position = position;
            Velocity = velocity;
            StateHash = stateHash;
            CustomFlags = customFlags;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && ActionId != 0U
                       && ServerTick.IsValid
                       && Position.IsFinite()
                       && Velocity.IsFinite();
            }
        }
    }
}
