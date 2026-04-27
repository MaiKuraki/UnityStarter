namespace CycloneGames.Networking.Serialization
{
    /// <summary>
    /// Available serializer types for network messages.
    /// </summary>
    public enum SerializerType
    {
        /// <summary>
        /// JSON serializer using Unity's JsonUtility.
        /// Supports managed types, universal compatibility.
        /// </summary>
        Json,

        /// <summary>
        /// JSON serializer using Newtonsoft.Json (Json.NET).
        /// Full JSON feature support including Dictionary, polymorphism, and custom converters.
        /// Requires com.unity.nuget.newtonsoft-json package.
        /// </summary>
        NewtonsoftJson,

        /// <summary>
        /// MessagePack-CSharp serializer.
        /// Excellent performance with minimal allocations.
        /// Requires [MessagePackObject] attribute on message types.
        /// </summary>
        MessagePack,

        /// <summary>
        /// Google Protocol Buffers serializer.
        /// Requires .proto schema and code generation.
        /// </summary>
        ProtoBuf,

        /// <summary>
        /// Google FlatBuffers serializer.
        /// Zero-copy deserialization for maximum read performance.
        /// Requires .fbs schema and code generation.
        /// </summary>
        FlatBuffers
    }
}
