using System;

namespace CycloneGames.Networking.Serialization
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
        /// Serializes the value into the provided writer. Allocation behavior depends on the
        /// concrete serializer and can include boxing when a value-type writer crosses this interface.
        /// </summary>
        void Serialize<T>(in T value, INetWriter writer) where T : struct;

        /// <summary>
        /// Deserializes a value from the provided read-only span.
        /// A span permits direct reads from caller-owned memory, but the concrete serializer may still allocate.
        /// </summary>
        /// <typeparam name="T">The struct type to deserialize.</typeparam>
        /// <param name="data">The input data.</param>
        /// <returns>The deserialized struct.</returns>
        T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct;

        /// <summary>
        /// Deserializes a value from the provided reader. Allocation behavior depends on the
        /// concrete serializer and can include boxing when a value-type reader crosses this interface.
        /// </summary>
        T Deserialize<T>(INetReader reader) where T : struct;
    }
}
