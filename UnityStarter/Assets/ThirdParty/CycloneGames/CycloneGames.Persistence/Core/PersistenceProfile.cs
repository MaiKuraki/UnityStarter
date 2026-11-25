using System;

namespace CycloneGames.Persistence
{
    public sealed class PersistenceProfile<T>
    {
        public PersistenceProfile(
            IPersistenceCodec<T> codec,
            PersistenceLimits limits = null)
        {
            Codec = codec ?? throw new ArgumentNullException(nameof(codec));
            Limits = limits ?? new PersistenceLimits();
            CodecId = codec.CodecId;
            if (string.IsNullOrEmpty(CodecId.Value))
            {
                throw new ArgumentException(
                    "The codec must expose a valid format token.",
                    nameof(codec));
            }
        }

        public IPersistenceCodec<T> Codec { get; }

        public PersistenceCodecId CodecId { get; }

        public PersistenceLimits Limits { get; }
    }
}
