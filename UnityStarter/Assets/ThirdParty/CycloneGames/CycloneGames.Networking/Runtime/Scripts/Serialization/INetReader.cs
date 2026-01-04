using System;

namespace CycloneGames.Networking.Serialization
{
    /// <summary>
    /// Zero-allocation reader interface for parsing network payloads.
    /// Implementations should operate directly on existing buffers without copying data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface is designed for high-performance network deserialization where
    /// minimizing garbage collection is critical. Implementations should work with
    /// pooled buffers (e.g., from <see cref="System.Buffers.ArrayPool{T}"/>).
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

        /// <summary>Reads a 32-bit floating-point number.</summary>
        float ReadFloat();

        /// <summary>
        /// Reads an unmanaged (blittable) struct directly from the buffer using unsafe memory copy.
        /// </summary>
        /// <typeparam name="T">
        /// An unmanaged type (value type with no reference type fields).
        /// Examples: int, float, Vector3, or any user-defined struct containing only unmanaged fields.
        /// </typeparam>
        /// <returns>The deserialized struct value.</returns>
        /// <remarks>
        /// <para>
        /// "Blittable" refers to types that have an identical representation in managed and unmanaged memory,
        /// allowing direct memory copy without marshalling. This provides zero-allocation deserialization.
        /// </para>
        /// <para>
        /// The <c>unmanaged</c> constraint in C# ensures the type contains no reference type fields
        /// and can be safely copied as raw bytes. This is equivalent to C/C++ POD (Plain Old Data) types.
        /// </para>
        /// </remarks>
        T ReadBlittable<T>() where T : unmanaged;

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
