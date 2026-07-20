using System;

namespace CycloneGames.Services
{
    /// <summary>
    /// Immutable allocation and compatibility policy for one settings store.
    /// </summary>
    public sealed class SettingsStoreOptions
    {
        public const int MinimumPayloadBytes = 256;
        public const int MaximumPayloadBytes = 16 * 1024 * 1024;
        public const int DefaultPayloadBytes = 256 * 1024;

        public static readonly SettingsStoreOptions Default = new SettingsStoreOptions();

        public SettingsStoreOptions(
            int maxPayloadBytes = DefaultPayloadBytes,
            bool allowLegacyPayload = false,
            bool clearTemporaryBuffers = true,
            bool allowModifiedPayload = false)
        {
            if (maxPayloadBytes < MinimumPayloadBytes || maxPayloadBytes > MaximumPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPayloadBytes),
                    maxPayloadBytes,
                    $"Payload budget must be between {MinimumPayloadBytes} and {MaximumPayloadBytes} bytes.");
            }

            MaxPayloadBytes = maxPayloadBytes;
            AllowLegacyPayload = allowLegacyPayload;
            ClearTemporaryBuffers = clearTemporaryBuffers;
            AllowModifiedPayload = allowModifiedPayload;
        }

        public int MaxPayloadBytes { get; }

        public bool AllowLegacyPayload { get; }

        public bool ClearTemporaryBuffers { get; }

        /// <summary>
        /// Allows a payload whose stored checksum does not match its bytes to proceed through
        /// deserialization, migration, and validation. The default rejects it before invoking
        /// the codec or observers.
        /// </summary>
        public bool AllowModifiedPayload { get; }
    }
}
