using System;
using System.Buffers;
using VYaml.Emitter;
using VYaml.Serialization;

namespace CycloneGames.Persistence.VYaml
{
    /// <summary>
    /// Serializes one explicitly generated VYaml contract without changing Persistence Record V1.
    /// </summary>
    public sealed class VYamlPersistenceCodec<T> : IPersistenceCodec<T>
    {
        private static readonly PersistenceCodecId StableCodecId =
            new PersistenceCodecId("vyaml/1");

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
