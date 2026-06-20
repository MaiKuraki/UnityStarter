namespace CycloneGames.Networking.Security
{
    public readonly struct NetworkReplayContext
    {
        public readonly int ConnectionId;
        public readonly ushort MessageId;
        public readonly uint Sequence;
        public readonly double TimeSeconds;

        public NetworkReplayContext(int connectionId, ushort messageId, uint sequence, double timeSeconds = 0d)
        {
            ConnectionId = connectionId;
            MessageId = messageId;
            Sequence = sequence;
            TimeSeconds = timeSeconds;
        }
    }

    public interface INetworkReplayProtector
    {
        bool TryAccept(in NetworkReplayContext context);
        void RemoveConnection(int connectionId);
        void Clear();
    }

    public sealed class NetworkReplayGuardProtector : INetworkReplayProtector
    {
        private readonly NetworkReplayGuard _guard;

        public NetworkReplayGuardProtector()
            : this(new NetworkReplayGuard())
        {
        }

        public NetworkReplayGuardProtector(NetworkReplayGuard guard)
        {
            _guard = guard ?? new NetworkReplayGuard();
        }

        public bool TryAccept(in NetworkReplayContext context)
        {
            return _guard.TryAccept(context.ConnectionId, context.MessageId, context.Sequence);
        }

        public void RemoveConnection(int connectionId)
        {
            _guard.RemoveConnection(connectionId);
        }

        public void Clear()
        {
            _guard.Clear();
        }
    }
}
