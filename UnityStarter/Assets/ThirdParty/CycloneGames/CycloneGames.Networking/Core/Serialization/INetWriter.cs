using System;

namespace CycloneGames.Networking.Serialization
{
    /// <summary>
    /// Buffer-oriented writer interface for building network payloads.
    /// Implementations should use explicit buffer ownership and bounded growth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations can work with pooled buffers (for example,
    /// <see cref="System.Buffers.ArrayPool{T}"/>). Interface dispatch can box a value-type
    /// implementation, so allocation behavior must be measured for the concrete call path.
    /// </para>
    /// <para>
    /// Thread Safety: Implementations are not required to be thread-safe.
    /// Each writer instance should be used by a single thread at a time.
    /// </para>
    /// </remarks>
    public interface INetWriter
    {
        /// <summary>Gets the current write position in the buffer.</summary>
        int Position { get; }

        /// <summary>Gets the total capacity of the underlying buffer.</summary>
        int Capacity { get; }

        /// <summary>Writes a single byte and advances the position.</summary>
        void WriteByte(byte value);

        /// <summary>Writes a span of bytes to the buffer.</summary>
        /// <param name="data">The bytes to write.</param>
        void WriteBytes(ReadOnlySpan<byte> data);

        /// <summary>Writes a 16-bit unsigned integer in little-endian format.</summary>
        void WriteUShort(ushort value);

        /// <summary>Writes a 32-bit signed integer in little-endian format.</summary>
        void WriteInt(int value);

        /// <summary>Writes a 32-bit unsigned integer in little-endian format.</summary>
        void WriteUInt(uint value);

        /// <summary>Writes a 64-bit signed integer in little-endian format.</summary>
        void WriteLong(long value);

        /// <summary>Writes a 64-bit unsigned integer in little-endian format.</summary>
        void WriteULong(ulong value);

        /// <summary>Writes a 32-bit floating-point number.</summary>
        void WriteFloat(float value);

        /// <summary>
        /// Returns an <see cref="ArraySegment{T}"/> representing the written portion of the buffer.
        /// </summary>
        /// <returns>An array segment from position 0 to the current write position.</returns>
        ArraySegment<byte> ToArraySegment();

        /// <summary>
        /// Resets the write position to 0 for reuse of the buffer.
        /// Does not clear the underlying data.
        /// </summary>
        void Reset();
    }
}
