using System;

namespace CycloneGames.Services
{
    /// <summary>
    /// Represents a primary settings entry that is already bound to a trusted storage location.
    /// Implementations may use files, browser storage, console save-data APIs, or another
    /// platform-specific persistence mechanism.
    /// </summary>
    public interface ISettingsStorage
    {
        /// <summary>
        /// Gets a diagnostic identifier for the bound entry. It must not contain credentials.
        /// </summary>
        string Location { get; }

        /// <summary>
        /// Gets the current byte length. A missing entry must be reported with
        /// <see cref="SettingsStorageEntryNotFoundException"/>; access, quota, mount, and other
        /// storage failures must not be collapsed into the missing-entry condition.
        /// </summary>
        long GetLength();

        /// <summary>
        /// Reads at most <paramref name="maxByteCount"/> bytes and transfers ownership of the
        /// returned non-null array to the caller. The returned length must not exceed the budget,
        /// and the caller may clear or otherwise modify the array. A concurrent disappearance
        /// must be reported with <see cref="SettingsStorageEntryNotFoundException"/>.
        /// </summary>
        byte[] Read(int maxByteCount);

        /// <summary>
        /// Commits a complete primary entry atomically. The implementation must consume the
        /// content synchronously, must not retain or mutate it, and must preserve the previous
        /// complete entry when the commit fails.
        /// </summary>
        void WriteAtomically(byte[] content);
    }

    /// <summary>
    /// Optional compatibility capability for the checksum sidecar used by Services 1.0 legacy
    /// YAML payloads. New storage adapters do not need to implement this interface.
    /// </summary>
    public interface ILegacySettingsChecksumStorage
    {
        /// <summary>
        /// Reads the legacy checksum and transfers ownership of the returned array to the caller.
        /// A missing sidecar must be reported with
        /// <see cref="SettingsStorageEntryNotFoundException"/>.
        /// </summary>
        byte[] ReadLegacyChecksum(int maxByteCount);

        /// <summary>
        /// Deletes the obsolete sidecar after the primary payload has been committed in the
        /// current envelope format. Missing sidecars must be treated as success.
        /// </summary>
        void DeleteLegacyChecksum();
    }

    /// <summary>
    /// Indicates that a bound storage entry is absent. Platform adapters should translate only
    /// their native not-found condition to this exception.
    /// </summary>
    public sealed class SettingsStorageEntryNotFoundException : Exception
    {
        public SettingsStorageEntryNotFoundException(string location)
            : base(CreateMessage(location))
        {
        }

        public SettingsStorageEntryNotFoundException(string location, Exception innerException)
            : base(CreateMessage(location), innerException)
        {
        }

        private static string CreateMessage(string location)
        {
            return string.IsNullOrWhiteSpace(location)
                ? "The settings storage entry was not found."
                : $"The settings storage entry '{location}' was not found.";
        }
    }
}
