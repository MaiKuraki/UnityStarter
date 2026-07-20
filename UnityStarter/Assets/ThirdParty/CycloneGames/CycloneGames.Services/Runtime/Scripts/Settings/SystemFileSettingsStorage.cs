using System;
using System.IO;
using CycloneGames.IO;

namespace CycloneGames.Services.Unity
{
    /// <summary>
    /// Binds one fully qualified file path to the platform-neutral settings storage contract.
    /// </summary>
    public sealed class SystemFileSettingsStorage :
        ISettingsStorage,
        ILegacySettingsChecksumStorage
    {
        private readonly SystemFileStore _fileStore;
        private readonly string _legacyChecksumPath;

        public SystemFileSettingsStorage(string absolutePath)
        {
            EnsurePlatformSupported();
            _fileStore = SystemFileStore.Default;

            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                throw new ArgumentException("A settings file path is required.", nameof(absolutePath));
            }

            if (!Path.IsPathFullyQualified(absolutePath))
            {
                throw new ArgumentException(
                    "The system file adapter requires a fully qualified path.",
                    nameof(absolutePath));
            }

            Location = Path.GetFullPath(absolutePath);
            _legacyChecksumPath = Location + ".checksum";
        }

        public string Location { get; }

        public long GetLength()
        {
            try
            {
                return _fileStore.GetLength(Location);
            }
            catch (Exception exception) when (IsMissing(exception))
            {
                throw new SettingsStorageEntryNotFoundException(Location, exception);
            }
        }

        public byte[] Read(int maxByteCount)
        {
            try
            {
                return _fileStore.ReadBytes(Location, maxByteCount);
            }
            catch (Exception exception) when (IsMissing(exception))
            {
                throw new SettingsStorageEntryNotFoundException(Location, exception);
            }
        }

        public void WriteAtomically(byte[] content)
        {
            _fileStore.WriteBytesAtomically(Location, content);
        }

        public byte[] ReadLegacyChecksum(int maxByteCount)
        {
            try
            {
                return _fileStore.ReadBytes(_legacyChecksumPath, maxByteCount);
            }
            catch (Exception exception) when (IsMissing(exception))
            {
                throw new SettingsStorageEntryNotFoundException(_legacyChecksumPath, exception);
            }
        }

        public void DeleteLegacyChecksum()
        {
            try
            {
                _fileStore.Delete(_legacyChecksumPath);
            }
            catch (Exception exception) when (IsMissing(exception))
            {
                // The compatibility sidecar is already absent.
            }
        }

        private static bool IsMissing(Exception exception)
        {
            return exception is FileNotFoundException
                || exception is DirectoryNotFoundException;
        }

        internal static void EnsurePlatformSupported()
        {
#if !UNITY_EDITOR && !UNITY_STANDALONE && !UNITY_IOS && !UNITY_ANDROID && !UNITY_SERVER
            throw new PlatformNotSupportedException(
                "The System.IO settings adapter is not enabled for this player platform. " +
                "Bind a platform save-data implementation to ISettingsStorage instead.");
#endif
        }
    }
}
