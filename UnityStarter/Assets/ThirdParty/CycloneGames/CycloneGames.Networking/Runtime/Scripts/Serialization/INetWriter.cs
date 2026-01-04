using System;

namespace CycloneGames.Networking.Serialization
{
    /// <summary>
    /// Zero-allocation writer interface for building network payloads.
    /// Implementations should use pooled buffers to minimize garbage collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface is designed for high-performance network serialization where
    /// minimizing garbage collection is critical. Implementations should work with
    /// pooled buffers (e.g., from <see cref="System.Buffers.ArrayPool{T}"/>).
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

        /// <summary>Writes a 32-bit floating-point number.</summary>
        void WriteFloat(float value);

        /// <summary>
        /// Writes an unmanaged (blittable) struct directly to the buffer using unsafe memory copy.
        /// </summary>
        /// <typeparam name="T">
        /// An unmanaged type (value type with no reference type fields).
        /// Examples: int, float, Vector3, or any user-defined struct containing only unmanaged fields.
        /// </typeparam>
        /// <param name="value">The struct value to write.</param>
        /// <remarks>
        /// <para>
        /// "Blittable" refers to types that have an identical representation in managed and unmanaged memory,
        /// allowing direct memory copy without marshalling. This provides zero-allocation serialization.
        /// </para>
        /// <para>
        /// The <c>unmanaged</c> constraint in C# ensures the type contains no reference type fields
        /// and can be safely copied as raw bytes. This is equivalent to C/C++ POD (Plain Old Data) types.
        /// </para>
        /// <para>
        /// <b>Important:</b> This method uses platform-specific endianness. For cross-platform
        /// compatibility, use the primitive write methods (WriteInt, WriteFloat, etc.) instead.
        /// </para>
        /// </remarks>
        void WriteBlittable<T>(in T value) where T : unmanaged;

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
