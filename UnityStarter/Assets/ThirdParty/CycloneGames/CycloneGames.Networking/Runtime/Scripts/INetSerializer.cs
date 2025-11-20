using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Abstraction for serializing network messages.
    /// </summary>
    public interface INetSerializer
    {
        /// <summary>
        /// Serializes the value into the provided byte array.
        /// </summary>
        /// <param name="value">The struct to serialize.</param>
        /// <param name="buffer">The target byte array.</param>
        /// <param name="offset">The start offset in the buffer.</param>
        /// <param name="writtenBytes">Number of bytes written.</param>
        void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct;

        /// <summary>
        /// Deserializes a value from the provided read-only span.
        /// Using Span allows for zero-copy reads from the network buffer.
        /// </summary>
        /// <typeparam name="T">The struct type to deserialize.</typeparam>
        /// <param name="data">The input data.</param>
        /// <returns>The deserialized struct.</returns>
        T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct;
    }
}