using System;

namespace CycloneGames.Networking.Security
{
    public readonly struct NetworkAntiCheatSignalId : IEquatable<NetworkAntiCheatSignalId>
    {
        public readonly string Value;

        public NetworkAntiCheatSignalId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Anti-cheat signal id must not be null or empty.", nameof(value));
            }

            Value = value;
        }

        public bool Equals(NetworkAntiCheatSignalId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkAntiCheatSignalId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }

    public static class NetworkAntiCheatSignalIds
    {
        public static readonly NetworkAntiCheatSignalId AuthenticationRejected = new NetworkAntiCheatSignalId("security.auth.rejected");
        public static readonly NetworkAntiCheatSignalId MessageRejected = new NetworkAntiCheatSignalId("security.message.rejected");
        public static readonly NetworkAntiCheatSignalId SignatureRejected = new NetworkAntiCheatSignalId("security.signature.rejected");
        public static readonly NetworkAntiCheatSignalId ReplayRejected = new NetworkAntiCheatSignalId("security.replay.rejected");
        public static readonly NetworkAntiCheatSignalId RateLimited = new NetworkAntiCheatSignalId("security.rate_limited");
        public static readonly NetworkAntiCheatSignalId AuthorityRejected = new NetworkAntiCheatSignalId("authority.rejected");
    }

    public readonly struct NetworkAntiCheatSignal
    {
        public readonly NetworkAntiCheatSignalId SignalId;
        public readonly NetworkReadinessSeverity Severity;
        public readonly int ConnectionId;
        public readonly ulong PlayerId;
        public readonly ushort MessageId;
        public readonly uint Sequence;
        public readonly double TimeSeconds;
        public readonly string Reason;

        public NetworkAntiCheatSignal(
            NetworkAntiCheatSignalId signalId,
            NetworkReadinessSeverity severity,
            int connectionId,
            ulong playerId,
            ushort messageId,
            uint sequence,
            double timeSeconds,
            string reason)
        {
            SignalId = signalId;
            Severity = severity;
            ConnectionId = connectionId;
            PlayerId = playerId;
            MessageId = messageId;
            Sequence = sequence;
            TimeSeconds = timeSeconds;
            Reason = reason ?? string.Empty;
        }
    }

    public interface INetworkAntiCheatSignalSink
    {
        void Report(in NetworkAntiCheatSignal signal);
    }

    public sealed class NoopNetworkAntiCheatSignalSink : INetworkAntiCheatSignalSink
    {
        public static readonly NoopNetworkAntiCheatSignalSink Instance = new NoopNetworkAntiCheatSignalSink();

        private NoopNetworkAntiCheatSignalSink()
        {
        }

        public void Report(in NetworkAntiCheatSignal signal)
        {
        }
    }

}
