using System;

namespace CycloneGames.Services
{
    public delegate void SettingsRefAction<T>(ref T settings) where T : struct;

    public delegate void SettingsChangedHandler<T>(in T settings, SettingsChangeReason reason)
        where T : struct;

    public enum SettingsChangeReason
    {
        Loaded = 0,
        Updated = 1,
        ResetToDefaults = 2
    }

    /// <summary>
    /// Converts one settings value to and from its persisted payload representation.
    /// Implementations must be deterministic for the same value and configuration.
    /// Serialize must not mutate or retain the supplied value and transfers ownership of its
    /// returned array to the caller. Deserialize must not retain the supplied memory and must
    /// return a value owned by the caller.
    /// </summary>
    public interface ISettingsCodec<T> where T : struct
    {
        byte[] Serialize(in T settings, int maxByteCount);

        T Deserialize(ReadOnlyMemory<byte> payload);
    }

    /// <summary>
    /// Indicates that serialization cannot complete within the caller-provided byte budget.
    /// </summary>
    public sealed class SettingsPayloadBudgetExceededException : Exception
    {
        public SettingsPayloadBudgetExceededException(int maxByteCount)
            : base($"The settings payload exceeds the {maxByteCount}-byte serialization budget.")
        {
            MaxByteCount = maxByteCount;
        }

        public int MaxByteCount { get; }
    }

    /// <summary>
    /// Owns defaults, independent snapshots, schema version discovery, validation, and
    /// forward migration for one settings type. GetVersion and Validate must not mutate or
    /// retain the supplied value.
    /// </summary>
    public interface ISettingsSchema<T> where T : struct
    {
        int CurrentVersion { get; }

        /// <summary>
        /// Creates a valid, independently owned default value. Mutable references must not be
        /// shared with schema-owned templates or earlier results.
        /// </summary>
        T CreateDefault();

        /// <summary>
        /// Creates an independent snapshot. Implementations must deep-copy every mutable
        /// reference reachable from the settings value.
        /// </summary>
        T Clone(in T settings);

        int GetVersion(in T settings);

        SettingsValidationResult Validate(in T settings);

        SettingsMigrationResult Migrate(
            int sourceVersion,
            int targetVersion,
            ref T settings);
    }
}
