namespace CycloneGames.Networking.Simulation
{
    public readonly struct NetworkActionCommand
    {
        public readonly ulong EntityId;
        public readonly uint ActionId;
        public readonly NetworkTickId ClientTick;
        public readonly NetworkTickId LastKnownServerTick;
        public readonly ushort Sequence;
        public readonly int PredictionKey;
        public readonly uint InputMask;
        public readonly uint CustomFlags;
        public readonly ulong PayloadHash;
        public readonly NetworkVector3 PrimaryVector;
        public readonly NetworkVector3 SecondaryVector;

        public NetworkActionCommand(
            ulong entityId,
            uint actionId,
            NetworkTickId clientTick,
            NetworkTickId lastKnownServerTick,
            ushort sequence,
            int predictionKey = 0,
            uint inputMask = 0U,
            uint customFlags = 0U,
            ulong payloadHash = 0UL,
            NetworkVector3 primaryVector = default,
            NetworkVector3 secondaryVector = default)
        {
            EntityId = entityId;
            ActionId = actionId;
            ClientTick = clientTick;
            LastKnownServerTick = lastKnownServerTick;
            Sequence = sequence;
            PredictionKey = predictionKey;
            InputMask = inputMask;
            CustomFlags = customFlags;
            PayloadHash = payloadHash;
            PrimaryVector = primaryVector;
            SecondaryVector = secondaryVector;
        }

        public bool HasPredictionKey
        {
            get
            {
                return PredictionKey != 0;
            }
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && ActionId != 0U
                       && ClientTick.IsValid
                       && PrimaryVector.IsFinite()
                       && SecondaryVector.IsFinite();
            }
        }
    }
}
