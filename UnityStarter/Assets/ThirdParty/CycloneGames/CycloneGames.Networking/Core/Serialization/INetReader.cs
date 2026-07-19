using System;

namespace CycloneGames.Networking.Serialization
{
    /// <summary>
    /// Buffer-oriented reader interface for parsing network payloads.
    /// Implementations should operate directly on existing buffers when their ownership allows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations can work with pooled buffers (for example,
    /// <see cref="System.Buffers.ArrayPool{T}"/>). Interface dispatch can box a value-type
    /// implementation, so allocation behavior must be measured for the concrete call path.
    /// </para>
    /// <para>
    /// Thread Safety: Implementations are not required to be thread-safe.
    /// Each reader instance should be used by a single thread at a time.
    /// </para>
    /// </remarks>
    public interface INetReader
    {
        /// <summary>Gets or sets the current read position in the buffer.</summary>
        int Position { get; set; }

        /// <summary>Gets the number of bytes remaining to be read.</summary>
        int Remaining { get; }

        /// <summary>Gets the total capacity of the underlying buffer.</summary>
        int Capacity { get; }

        /// <summary>Reads a single byte and advances the position.</summary>
        byte ReadByte();

        /// <summary>
        /// Reads a specified number of bytes into the destination span.
        /// </summary>
        /// <param name="destination">The span to copy bytes into.</param>
        /// <param name="count">The number of bytes to read.</param>
        void ReadBytes(Span<byte> destination, int count);

        /// <summary>Reads a 16-bit unsigned integer in little-endian format.</summary>
        ushort ReadUShort();

        /// <summary>Reads a 32-bit signed integer in little-endian format.</summary>
        int ReadInt();

        /// <summary>Reads a 32-bit unsigned integer in little-endian format.</summary>
        uint ReadUInt();

        /// <summary>Reads a 64-bit signed integer in little-endian format.</summary>
        long ReadLong();

        /// <summary>Reads a 64-bit unsigned integer in little-endian format.</summary>
        ulong ReadULong();

        /// <summary>Reads a 32-bit floating-point number.</summary>
        float ReadFloat();

        /// <summary>
        /// Returns a read-only span over a portion of the buffer without copying data.
        /// </summary>
        /// <param name="count">The number of bytes to slice.</param>
        /// <returns>A span representing the requested bytes.</returns>
        /// <remarks>
        /// The returned span is only valid while the underlying buffer remains allocated.
        /// Do not store references to this span beyond the current operation.
        /// </remarks>
        ReadOnlySpan<byte> ReadBytesSpan(int count);
    }
}
