using System;
using System.Buffers;
using VYaml.Emitter;
using VYaml.Serialization;

namespace CycloneGames.Persistence.VYaml
{
    /// <summary>
    /// VYaml codec for one UTF-8 LF wire format. Resolves formatters from the caller's generated
    /// resolver, then from VYaml standard and Unity formatters.
    /// </summary>
    public sealed class VYamlPersistenceCodec<T> : IPersistenceCodec<T>
    {
        private static readonly PersistenceCodecId StableCodecId =
            new PersistenceCodecId("vyaml-utf8-lf");

        private readonly YamlSerializerOptions _serializerOptions;

        public VYamlPersistenceCodec(IYamlFormatterResolver primaryResolver)
        {
            if (primaryResolver == null)
            {
                throw new ArgumentNullException(nameof(primaryResolver));
            }

            if (primaryResolver.GetFormatter<T>() == null)
            {
                throw new ArgumentException(
                    $"The supplied VYaml resolver does not contain a generated formatter for {typeof(T).FullName}.",
                    nameof(primaryResolver));
            }

            _serializerOptions = new YamlSerializerOptions
            {
                Resolver = new PersistenceYamlResolver(primaryResolver)
            };
        }

        public PersistenceCodecId CodecId => StableCodecId;

        public void Serialize(
            in T value,
            IBufferWriter<byte> destination,
            in PersistenceWriteContext context)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            var emitter = new Utf8YamlEmitter(destination);
            YamlSerializer.Serialize(ref emitter, value, _serializerOptions);
        }

        public T Deserialize(
            ReadOnlyMemory<byte> payload,
            in PersistenceReadContext context)
        {
            return YamlSerializer.Deserialize<T>(payload, _serializerOptions);
        }

        // Resolution order is fixed: generated DTO formatters, VYaml primitives, then Unity types.
        // Built-in fallbacks must never shadow a formatter supplied by the caller.
        private sealed class PersistenceYamlResolver : IYamlFormatterResolver
        {
            private readonly IYamlFormatterResolver _primaryResolver;

            internal PersistenceYamlResolver(IYamlFormatterResolver primaryResolver)
            {
                _primaryResolver = primaryResolver;
            }

            public IYamlFormatter<TValue> GetFormatter<TValue>()
            {
                IYamlFormatter<TValue> formatter = _primaryResolver.GetFormatter<TValue>();
                if (formatter != null)
                {
                    return formatter;
                }

                formatter = StandardResolver.Instance.GetFormatter<TValue>();
                if (formatter != null)
                {
                    return formatter;
                }

                return UnityResolver.Instance.GetFormatter<TValue>();
            }
        }
    }
}
