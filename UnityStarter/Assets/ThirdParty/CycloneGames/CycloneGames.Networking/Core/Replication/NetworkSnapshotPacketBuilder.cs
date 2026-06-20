using System;
using CycloneGames.Hash.Core;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.Replication
{
    [Flags]
    public enum NetworkSnapshotEntryFlags : byte
    {
        None = 0,
        FullState = 1 << 0,
        Delta = 1 << 1
    }

    public readonly struct NetworkSnapshotWriteResult
    {
        public readonly int ObjectCount;
        public readonly int FullStateCount;
        public readonly int DeltaCount;
        public readonly int BytesWritten;
        public readonly ulong AggregatePayloadHash;

        public NetworkSnapshotWriteResult(
            int objectCount,
            int fullStateCount,
            int deltaCount,
            int bytesWritten,
            ulong aggregatePayloadHash)
        {
            ObjectCount = objectCount;
            FullStateCount = fullStateCount;
            DeltaCount = deltaCount;
            BytesWritten = bytesWritten;
            AggregatePayloadHash = aggregatePayloadHash;
        }
    }

    public interface INetworkSnapshotPayloadSource
    {
        int GetPayloadSize(int sourceIndex, bool fullState);
        ulong GetPayloadHash(int sourceIndex, bool fullState);
        void WritePayload(int sourceIndex, bool fullState, INetWriter writer);
    }

    public sealed class NetworkSnapshotPacketBuilder
    {
        public const byte PROTOCOL_VERSION = 1;
        private const ulong FnvOffsetBasis = Fnv1a64.OffsetBasis;
        private const ulong FnvPrime = Fnv1a64.Prime;

        public NetworkSnapshotWriteResult WriteSnapshot(
            ReadOnlySpan<NetworkReplicationSelection> selections,
            int serverTick,
            INetworkSnapshotPayloadSource payloadSource,
            INetWriter writer)
        {
            if (serverTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(serverTick));
            }

            if (payloadSource == null)
            {
                throw new ArgumentNullException(nameof(payloadSource));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (selections.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(selections));
            }

            int start = writer.Position;
            int fullStateCount = 0;
            int deltaCount = 0;
            ulong aggregateHash = FnvOffsetBasis;

            writer.WriteByte(PROTOCOL_VERSION);
            writer.WriteInt(serverTick);
            writer.WriteUShort((ushort)selections.Length);

            for (int i = 0; i < selections.Length; i++)
            {
                NetworkReplicationSelection selection = selections[i];
                bool fullState = selection.RequiresFullState;
                int payloadSize = payloadSource.GetPayloadSize(selection.SourceIndex, fullState);
                if (payloadSize < 0)
                {
                    throw new InvalidOperationException("Snapshot payload size must not be negative.");
                }

                ulong payloadHash = payloadSource.GetPayloadHash(selection.SourceIndex, fullState);
                NetworkSnapshotEntryFlags flags = fullState
                    ? NetworkSnapshotEntryFlags.FullState
                    : NetworkSnapshotEntryFlags.Delta;

                WriteULong(writer, selection.ObjectId);
                writer.WriteByte((byte)flags);
                writer.WriteByte((byte)selection.Channel);
                writer.WriteInt(payloadSize);

                int payloadStart = writer.Position;
                payloadSource.WritePayload(selection.SourceIndex, fullState, writer);
                int actualPayloadSize = writer.Position - payloadStart;
                if (actualPayloadSize != payloadSize)
                {
                    throw new InvalidOperationException("Snapshot payload writer produced a different byte count than declared.");
                }

                if (fullState)
                {
                    fullStateCount++;
                }
                else
                {
                    deltaCount++;
                }

                aggregateHash = Combine(aggregateHash, selection.ObjectId);
                aggregateHash = Combine(aggregateHash, payloadHash);
                aggregateHash = Combine(aggregateHash, payloadSize);
            }

            return new NetworkSnapshotWriteResult(
                selections.Length,
                fullStateCount,
                deltaCount,
                writer.Position - start,
                aggregateHash == 0UL ? FnvOffsetBasis : aggregateHash);
        }

        private static void WriteULong(INetWriter writer, ulong value)
        {
            writer.WriteUInt((uint)value);
            writer.WriteUInt((uint)(value >> 32));
        }

        private static ulong Combine(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= FnvPrime;
                hash ^= (uint)(value >> 16);
                hash *= FnvPrime;
                return hash;
            }
        }

        private static ulong Combine(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= FnvPrime;
                }

                return hash;
            }
        }
    }
}
