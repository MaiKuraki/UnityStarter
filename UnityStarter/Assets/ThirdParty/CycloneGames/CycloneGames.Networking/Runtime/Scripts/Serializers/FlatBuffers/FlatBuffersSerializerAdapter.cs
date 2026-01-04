#if FLATBUFFERS
using System;
using CycloneGames.Networking.Serialization;
using FlatBuffers;

namespace CycloneGames.Networking.Serializer.FlatBuffers
{
    /// <summary>
    /// FlatBuffers serializer adapter.
    /// FlatBuffers provides zero-copy deserialization for maximum performance.
    /// Note: FlatBuffers requires schema compilation - use flatc compiler to generate C# code.
    /// 
    /// Usage Pattern:
    /// 1. Define your schema (.fbs file)
    /// 2. Generate C# code with flatc
    /// 3. Use generated types directly with this adapter
    /// </summary>
    public sealed class FlatBuffersSerializerAdapter : INetSerializer
    {
        /// <summary>
        /// Thread-local FlatBufferBuilder to avoid allocation.
        /// </summary>
        [ThreadStatic]
        private static FlatBufferBuilder _builder;

        private static FlatBufferBuilder GetBuilder()
        {
            return _builder ??= new FlatBufferBuilder(1024);
        }

        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            // FlatBuffers requires generated types with Finish() method
            // This is a generic placeholder - actual implementation depends on generated code
            throw new NotSupportedException(
                "FlatBuffers serialization requires generated table types. " +
                "Use FlatBuffersSerializerAdapter<T> with your specific generated type, " +
                "or serialize directly using the generated API.");
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            throw new NotSupportedException(
                "FlatBuffers serialization requires generated table types.");
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            // FlatBuffers deserializes to generated struct wrappers
            // Zero-copy access to the underlying buffer
            throw new NotSupportedException(
                "FlatBuffers deserialization requires generated accessor types. " +
                "Access the buffer directly using generated GetRootAs*() methods.");
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            var span = reader.ReadBytesSpan(reader.Remaining);
            return Deserialize<T>(span);
        }

        /// <summary>
        /// Helper method to get raw buffer for FlatBuffers zero-copy deserialization.
        /// Use with generated GetRootAs*() methods.
        /// </summary>
        public static ByteBuffer WrapBuffer(ArraySegment<byte> data)
        {
            return new ByteBuffer(data.Array, data.Offset);
        }

        public static ByteBuffer WrapBuffer(byte[] data, int offset = 0)
        {
            return new ByteBuffer(data, offset);
        }
    }

    /// <summary>
    /// Helper for creating FlatBuffers messages.
    /// Use the thread-local builder to avoid allocations.
    /// </summary>
    public static class FlatBuffersHelper
    {
        [ThreadStatic]
        private static FlatBufferBuilder _builder;

        /// <summary>
        /// Get thread-local builder. Call Clear() before each new message.
        /// </summary>
        public static FlatBufferBuilder Builder
        {
            get
            {
                if (_builder == null)
                    _builder = new FlatBufferBuilder(1024);
                return _builder;
            }
        }

        /// <summary>
        /// Extract finished buffer as ArraySegment without copying.
        /// </summary>
        public static ArraySegment<byte> GetBufferSegment(FlatBufferBuilder builder)
        {
            return builder.DataBuffer.ToArraySegment(builder.DataBuffer.Position, builder.Offset);
        }
    }
}
#endif
