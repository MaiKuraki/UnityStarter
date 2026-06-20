using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementInputCommandMessage
    {
        public ulong EntityId;
        public int ClientTick;
        public int LastReceivedServerTick;
        public ushort InputSequence;
        public uint ButtonMask;
        public uint CustomFlags;
        public float DeltaTime;
        public NetworkVector3 MoveAxes;
        public NetworkVector3 AimDirection;

        public MovementInputCommandMessage(
            ulong entityId,
            int clientTick,
            int lastReceivedServerTick,
            ushort inputSequence,
            uint buttonMask,
            uint customFlags,
            float deltaTime,
            NetworkVector3 moveAxes,
            NetworkVector3 aimDirection)
        {
            EntityId = entityId;
            ClientTick = clientTick;
            LastReceivedServerTick = lastReceivedServerTick;
            InputSequence = inputSequence;
            ButtonMask = buttonMask;
            CustomFlags = customFlags;
            DeltaTime = deltaTime;
            MoveAxes = moveAxes;
            AimDirection = aimDirection;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && ClientTick >= 0
                       && LastReceivedServerTick >= 0
                       && DeltaTime >= 0f
                       && DeltaTime <= MovementNetworkProtocol.MAX_INPUT_DELTA_TIME
                       && MoveAxes.IsFinite()
                       && AimDirection.IsFinite();
            }
        }

        public bool HasButton(uint buttonMask)
        {
            return (ButtonMask & buttonMask) == buttonMask;
        }
    }
}
