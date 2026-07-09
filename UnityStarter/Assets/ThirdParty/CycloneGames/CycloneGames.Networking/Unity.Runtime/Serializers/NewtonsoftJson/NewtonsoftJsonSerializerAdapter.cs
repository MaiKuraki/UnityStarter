#if NEWTONSOFT_JSON
using System;
using System.Buffers;
using System.Text;
using Newtonsoft.Json;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.Serializer.NewtonsoftJson
{
    /// <summary>
    /// Newtonsoft.Json serializer adapter for network messages.
    /// Provides full JSON feature support including Dictionary, polymorphism, and custom converters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This adapter is recommended when:
    /// - You need Dictionary/HashSet serialization (JsonUtility doesn't support these)
    /// - You need polymorphic type handling with $type
    /// - You need to communicate with non-Unity backends
    /// - You need custom JsonConverter support
    /// </para>
    /// <para>
    /// For maximum performance with binary format, consider using MessagePackSerializerAdapter instead.
    /// </para>
    /// </remarks>
    public sealed class NewtonsoftJsonSerializerAdapter : INetSerializer
    {
        private const int STACKALLOC_THRESHOLD = 512;

        private static readonly Lazy<NewtonsoftJsonSerializerAdapter> _instance =
            new Lazy<NewtonsoftJsonSerializerAdapter>(() => new NewtonsoftJsonSerializerAdapter());

        /// <summary>
        /// Gets the singleton instance with default settings.
        /// </summary>
        public static NewtonsoftJsonSerializerAdapter Instance => _instance.Value;

        private readonly JsonSerializerSettings _settings;
        private readonly Encoding _encoding = Encoding.UTF8;

        /// <summary>
        /// Creates a new adapter with default settings.
        /// </summary>
        public NewtonsoftJsonSerializerAdapter() : this(CreateDefaultSettings())
        {
        }

        /// <summary>
        /// Creates a new adapter with custom settings.
        /// </summary>
        /// <param name="settings">Custom Newtonsoft.Json settings.</param>
        public NewtonsoftJsonSerializerAdapter(JsonSerializerSettings settings)
        {
            _settings = settings ?? CreateDefaultSettings();
        }

        private static JsonSerializerSettings CreateDefaultSettings()
        {
            return new JsonSerializerSettings
            {
                // Optimize for network transmission
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                // Type name handling disabled by default for security
                TypeNameHandling = TypeNameHandling.None
            };
        }

        /// <summary>
        /// Creates an adapter with polymorphic type support enabled.
        /// </summary>
        /// <remarks>
        /// Warning: TypeNameHandling can be a security risk if deserializing untrusted data.
        /// Only use with trusted message sources.
        /// </remarks>
        public static NewtonsoftJsonSerializerAdapter CreateWithTypeHandling()
        {
            var settings = CreateDefaultSettings();
            settings.TypeNameHandling = TypeNameHandling.Auto;
            return new NewtonsoftJsonSerializerAdapter(settings);
        }

        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            string json = JsonConvert.SerializeObject(value, _settings);
            int byteCount = _encoding.GetByteCount(json);

            if (offset + byteCount > buffer.Length)
            {
                throw new ArgumentException(
                    $"Buffer too small. Need {byteCount} bytes, have {buffer.Length - offset}");
            }

            writtenBytes = _encoding.GetBytes(json, 0, json.Length, buffer, offset);
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            string json = JsonConvert.SerializeObject(value, _settings);
            WriteUtf8String(json, writer);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            string json = _encoding.GetString(data);
            return JsonConvert.DeserializeObject<T>(json, _settings);
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            var span = reader.ReadBytesSpan(reader.Remaining);
            return Deserialize<T>(span);
        }

        private void WriteUtf8String(string json, INetWriter writer)
        {
            int byteCount = _encoding.GetByteCount(json);
            if (byteCount == 0)
            {
                writer.WriteBytes(ReadOnlySpan<byte>.Empty);
                return;
            }

            if (byteCount <= STACKALLOC_THRESHOLD)
            {
                Span<byte> bytes = stackalloc byte[byteCount];
                int writtenBytes = _encoding.GetBytes(json.AsSpan(), bytes);
                writer.WriteBytes(bytes.Slice(0, writtenBytes));
                return;
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int writtenBytes = _encoding.GetBytes(json, 0, json.Length, rented, 0);
                writer.WriteBytes(new ReadOnlySpan<byte>(rented, 0, writtenBytes));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
#endif
