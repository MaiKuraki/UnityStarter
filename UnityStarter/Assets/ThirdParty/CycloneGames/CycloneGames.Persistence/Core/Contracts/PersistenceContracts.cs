using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.Persistence
{
    /// <summary>
    /// Converts one application value to and from an explicitly versioned payload.
    /// Implementations must not retain the value, destination, payload, or contexts.
    /// T must be pure persisted data and must not own disposable or thread-affine resources.
    /// </summary>
    public interface IPersistenceCodec<T>
    {
        PersistenceCodecId CodecId { get; }

        void Serialize(
            in T value,
            IBufferWriter<byte> destination,
            in PersistenceWriteContext context);

        T Deserialize(
            ReadOnlyMemory<byte> payload,
            in PersistenceReadContext context);
    }

    /// <summary>
    /// Owns one storage location and transfers ownership of successful read buffers to the caller.
    /// </summary>
    public interface IPersistenceStorage
    {
        string Location { get; }

        Task<PersistenceStorageReadResult> ReadAsync(
            int maxByteCount,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Borrows <paramref name="content"/> until the returned task completes.
        /// The implementation must not retain or modify the array.
        /// </summary>
        Task WriteAtomicallyAsync(
            byte[] content,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Thrown by a bounded writer before a codec can grow beyond the configured payload budget.
    /// </summary>
    public sealed class PersistencePayloadBudgetExceededException : Exception
    {
        public PersistencePayloadBudgetExceededException(int maximumPayloadBytes)
            : base($"The persistence payload exceeded the {maximumPayloadBytes}-byte limit.")
        {
            MaximumPayloadBytes = maximumPayloadBytes;
        }

        public int MaximumPayloadBytes { get; }
    }
}
