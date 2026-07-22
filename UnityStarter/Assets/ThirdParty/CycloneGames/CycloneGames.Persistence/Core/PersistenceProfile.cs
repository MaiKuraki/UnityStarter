using System;

namespace CycloneGames.Persistence
{
    /// <summary>
    /// Immutable composition of one codec and one bounded Record V1 policy.
    /// </summary>
    public sealed class PersistenceProfile<T>
    {
        public PersistenceProfile(
            IPersistenceCodec<T> codec,
            PersistenceLimits limits = null)
        {
            Codec = codec ?? throw new ArgumentNullException(nameof(codec));
            Limits = limits ?? PersistenceLimits.Default;
            CodecId = codec.CodecId;
            if (string.IsNullOrEmpty(CodecId.Value))
            {
                throw new ArgumentException(
                    "The codec must expose a valid, versioned identifier.",
                    nameof(codec));
            }
        }

        public IPersistenceCodec<T> Codec { get; }

        public PersistenceCodecId CodecId { get; }

        public PersistenceLimits Limits { get; }
    }
}
