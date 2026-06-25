namespace CycloneGames.RPGFoundation.Movement.Core
{
    public enum MovementStateRequestSource
    {
        Direct,
        Input,
        Ability,
        Pathfinding,
        Network,
        Internal
    }

    public readonly struct MovementStateRequestContext
    {
        public MovementStateRequestSource Source { get; }
        public object Payload { get; }
        public int Tick { get; }
        public ushort Sequence { get; }
        public int PredictionKey { get; }
        public uint Flags { get; }

        public bool IsAbilityDriven => Source == MovementStateRequestSource.Ability;
        public bool IsNetworkDriven => Source == MovementStateRequestSource.Network;
        public bool HasPredictionKey => PredictionKey != 0;
        public bool HasTimeline => Tick >= 0 || Sequence != 0 || PredictionKey != 0;

        public MovementStateRequestContext(
            MovementStateRequestSource source,
            object payload = null,
            int tick = -1,
            ushort sequence = 0,
            int predictionKey = 0,
            uint flags = 0U)
        {
            Source = source;
            Payload = payload;
            Tick = tick;
            Sequence = sequence;
            PredictionKey = predictionKey;
            Flags = flags;
        }

        public static MovementStateRequestContext FromAbility(object payload = null)
        {
            return new MovementStateRequestContext(MovementStateRequestSource.Ability, payload);
        }

        public static MovementStateRequestContext FromInput(object payload = null)
        {
            return new MovementStateRequestContext(MovementStateRequestSource.Input, payload);
        }

        public static MovementStateRequestContext FromNetwork(
            object payload = null,
            int tick = -1,
            ushort sequence = 0,
            int predictionKey = 0,
            uint flags = 0U)
        {
            return new MovementStateRequestContext(
                MovementStateRequestSource.Network,
                payload,
                tick,
                sequence,
                predictionKey,
                flags);
        }
    }
}
