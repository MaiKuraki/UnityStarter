using System;

namespace CycloneGames.Networking.Serialization
{
    /// <summary>
    /// Factory for creating serializer instances.
    /// Uses conditional compilation to only expose available serializers.
    /// </summary>
    public static class SerializerFactory
    {
        /// <summary>
        /// Create a serializer of the specified type.
        /// Throws if the serializer type is not available (package not installed).
        /// </summary>
        public static INetSerializer Create(SerializerType type)
        {
            return type switch
            {
                SerializerType.Json => JsonSerializerAdapter.Instance,
                SerializerType.NewtonsoftJson => CreateNewtonsoftJsonSerializer(),
                SerializerType.MessagePack => CreateMessagePackSerializer(),
                SerializerType.ProtoBuf => CreateProtoBufSerializer(),
                SerializerType.FlatBuffers => CreateFlatBuffersSerializer(),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown serializer type")
            };
        }

        /// <summary>
        /// Check if a serializer type is available (compiled in).
        /// </summary>
        public static bool IsAvailable(SerializerType type)
        {
            return type switch
            {
                SerializerType.Json => true,
#if NEWTONSOFT_JSON
                SerializerType.NewtonsoftJson => true,
#else
                SerializerType.NewtonsoftJson => false,
#endif
#if MESSAGEPACK
                SerializerType.MessagePack => true,
#else
                SerializerType.MessagePack => false,
#endif
#if PROTOBUF
                SerializerType.ProtoBuf => true,
#else
                SerializerType.ProtoBuf => false,
#endif
#if FLATBUFFERS
                SerializerType.FlatBuffers => true,
#else
                SerializerType.FlatBuffers => false,
#endif
                _ => false
            };
        }

        /// <summary>
        /// Get the recommended serializer based on available packages.
        /// Priority: MessagePack > NewtonsoftJson > Json
        /// </summary>
        public static INetSerializer GetRecommended()
        {
#if MESSAGEPACK
            return CreateMessagePackSerializer();
#elif NEWTONSOFT_JSON
            return CreateNewtonsoftJsonSerializer();
#else
            return JsonSerializerAdapter.Instance;
#endif
        }

        /// <summary>
        /// Get the default serializer (Json).
        /// </summary>
        public static INetSerializer GetDefault()
        {
            return JsonSerializerAdapter.Instance;
        }

        private static INetSerializer CreateNewtonsoftJsonSerializer()
        {
#if NEWTONSOFT_JSON
            return Serializer.NewtonsoftJson.NewtonsoftJsonSerializerAdapter.Instance;
#else
            throw new NotSupportedException(
                "Newtonsoft.Json serializer is not available. Install com.unity.nuget.newtonsoft-json package.");
#endif
        }

        private static INetSerializer CreateMessagePackSerializer()
        {
#if MESSAGEPACK
            return new Serializer.MessagePack.MessagePackSerializerAdapter();
#else
            throw new NotSupportedException(
                "MessagePack serializer is not available. Install com.github.messagepack-csharp package.");
#endif
        }

        private static INetSerializer CreateProtoBufSerializer()
        {
#if PROTOBUF
            return new Serializer.ProtoBuf.ProtoBufSerializerAdapter();
#else
            throw new NotSupportedException(
                "ProtoBuf serializer is not available. Install Google.Protobuf package and add PROTOBUF to scripting defines.");
#endif
        }

        private static INetSerializer CreateFlatBuffersSerializer()
        {
#if FLATBUFFERS
            return new Serializer.FlatBuffers.FlatBuffersSerializerAdapter();
#else
            throw new NotSupportedException(
                "FlatBuffers serializer is not available. Install com.google.flatbuffers package and add FLATBUFFERS to scripting defines.");
#endif
        }
    }
}
