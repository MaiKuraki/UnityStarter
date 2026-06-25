using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class MovementNetworkInputHistory
    {
        private readonly NetworkActionHistory<MovementInputCommandMessage> _history;

        public MovementNetworkInputHistory(int capacity)
        {
            _history = new NetworkActionHistory<MovementInputCommandMessage>(capacity);
        }

        public int Capacity
        {
            get
            {
                return _history.Capacity;
            }
        }

        public int Count
        {
            get
            {
                return _history.Count;
            }
        }

        public void Record(in MovementInputCommandMessage command)
        {
            _history.Record(
                command.EntityId,
                new NetworkTickId(command.ClientTick),
                command.InputSequence,
                command);
        }

        public bool TryGet(
            ulong entityId,
            int clientTick,
            ushort inputSequence,
            out MovementInputCommandMessage command)
        {
            return _history.TryGet(
                entityId,
                new NetworkTickId(clientTick),
                inputSequence,
                out command);
        }

        public bool TryGetLatest(
            ulong entityId,
            out MovementInputCommandMessage command,
            out int clientTick,
            out ushort inputSequence)
        {
            if (_history.TryGetLatest(entityId, out NetworkActionHistoryEntry<MovementInputCommandMessage> entry))
            {
                command = entry.Snapshot;
                clientTick = (int)entry.Tick.Value;
                inputSequence = entry.Sequence;
                return true;
            }

            command = default;
            clientTick = -1;
            inputSequence = 0;
            return false;
        }

        public bool Contains(in MovementInputCommandMessage command)
        {
            return TryGet(command.EntityId, command.ClientTick, command.InputSequence, out _);
        }

        public int RemoveEntity(ulong entityId)
        {
            return _history.RemoveEntity(entityId);
        }

        public void Clear()
        {
            _history.Clear();
        }
    }
}
