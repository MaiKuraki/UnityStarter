using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Aggregate content fingerprint of a <see cref="DataTableManifest"/>, deterministic at the
    /// byte level across platforms, runtimes, and process architectures. Two endpoints that
    /// compute the same fingerprint hold the same declared configuration identity; any single-bit
    /// difference in a hashed field yields a different fingerprint.
    ///
    /// Byte layout (format tag "DTF1", all integers little-endian, all text UTF-8):
    /// <code>
    /// header:      "DTF1" (4 ASCII bytes) | int32 schemaVersion | int32 entryCount
    /// per entry (sorted by normalized table name, StringComparer.Ordinal):
    ///              int32 nameByteCount | name UTF-8 bytes
    ///              byte flags (bit0 = has expected byte length, bit1 = has SHA-256)
    ///              if bit0: int64 expectedByteLength
    ///              if bit1: 64 UTF-8 bytes of lowercase SHA-256 hex
    /// </code>
    /// Excluded by design: Location (an environment-specific path, not content identity) and
    /// Required (a local admission policy, not content identity). An empty manifest hashes its
    /// header only, so an empty and a missing manifest never collide.
    ///
    /// Cold-path allocation: one entry-array copy for sorting, one reusable name buffer, and the
    /// final 64-character hex string. No per-entry allocation.
    /// </summary>
    public readonly struct DataTableConfigFingerprint : IEquatable<DataTableConfigFingerprint>
    {
        private const string FORMAT_TAG = "DTF1";
        private const int SHA256_HEX_LENGTH = 64;

        private static readonly byte[] FormatTagBytes = Encoding.ASCII.GetBytes(FORMAT_TAG);

        private readonly string _hex;

        private DataTableConfigFingerprint(string hex)
        {
            _hex = hex;
        }

        /// <summary>
        /// True only for the default (uninitialized) value, which never results from
        /// <see cref="Compute"/>.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(_hex);

        public static DataTableConfigFingerprint Compute(DataTableManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            IReadOnlyList<DataTableManifestEntry> source = manifest.Entries;
            int count = source.Count;
            var entries = new DataTableManifestEntry[count];
            for (int i = 0; i < count; i++)
            {
                entries[i] = source[i];
            }

            Array.Sort(entries, TableNameOrdinalComparer.Instance);

            int nameBufferLength = SHA256_HEX_LENGTH;
            for (int i = 0; i < count; i++)
            {
                int byteCount = Encoding.UTF8.GetByteCount(entries[i].TableName);
                if (byteCount > nameBufferLength)
                {
                    nameBufferLength = byteCount;
                }
            }

            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var scratch = new byte[8];
                var nameBuffer = new byte[nameBufferLength];

                hash.AppendData(FormatTagBytes, 0, FormatTagBytes.Length);
                WriteInt32LittleEndian(scratch, manifest.SchemaVersion);
                hash.AppendData(scratch, 0, 4);
                WriteInt32LittleEndian(scratch, count);
                hash.AppendData(scratch, 0, 4);

                for (int i = 0; i < count; i++)
                {
                    DataTableManifestEntry entry = entries[i];

                    int nameBytes = Encoding.UTF8.GetBytes(
                        entry.TableName, 0, entry.TableName.Length, nameBuffer, 0);
                    WriteInt32LittleEndian(scratch, nameBytes);
                    hash.AppendData(scratch, 0, 4);
                    hash.AppendData(nameBuffer, 0, nameBytes);

                    byte flags = 0;
                    if (entry.HasExpectedByteLength)
                    {
                        flags |= 1;
                    }

                    if (entry.HasSha256Hash)
                    {
                        flags |= 2;
                    }

                    scratch[0] = flags;
                    hash.AppendData(scratch, 0, 1);

                    if (entry.HasExpectedByteLength)
                    {
                        WriteInt64LittleEndian(scratch, entry.ExpectedByteLength);
                        hash.AppendData(scratch, 0, 8);
                    }

                    if (entry.HasSha256Hash)
                    {
                        // Sha256Hex is validated lowercase hex (ASCII), so UTF-8 is a 1:1 mapping.
                        int hexBytes = Encoding.UTF8.GetBytes(
                            entry.Sha256Hex, 0, entry.Sha256Hex.Length, nameBuffer, 0);
                        hash.AppendData(nameBuffer, 0, hexBytes);
                    }
                }

                return new DataTableConfigFingerprint(
                    DataTableHashUtility.ToLowerHex(hash.GetHashAndReset()));
            }
        }

        /// <summary>
        /// Fixed-time ordinal comparison of the underlying hex digests. Two default values are
        /// equal; a default value never equals a computed fingerprint.
        /// </summary>
        public bool Equals(DataTableConfigFingerprint other)
        {
            return DataTableHashUtility.FixedTimeEqualsHex(_hex, other._hex);
        }

        public override bool Equals(object obj)
        {
            return obj is DataTableConfigFingerprint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hex == null ? 0 : StringComparer.Ordinal.GetHashCode(_hex);
        }

        /// <summary>
        /// Returns the lowercase hexadecimal digest, or an empty string for the default value.
        /// </summary>
        public override string ToString()
        {
            return _hex ?? string.Empty;
        }

        public static bool operator ==(DataTableConfigFingerprint left, DataTableConfigFingerprint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DataTableConfigFingerprint left, DataTableConfigFingerprint right)
        {
            return !left.Equals(right);
        }

        // Explicit shifts keep the byte layout little-endian on every host architecture;
        // BitConverter would follow host endianness and break cross-platform determinism.
        private static void WriteInt32LittleEndian(byte[] target, int value)
        {
            target[0] = (byte)value;
            target[1] = (byte)(value >> 8);
            target[2] = (byte)(value >> 16);
            target[3] = (byte)(value >> 24);
        }

        private static void WriteInt64LittleEndian(byte[] target, long value)
        {
            for (int i = 0; i < 8; i++)
            {
                target[i] = (byte)(value >> (8 * i));
            }
        }

        private sealed class TableNameOrdinalComparer : IComparer<DataTableManifestEntry>
        {
            internal static readonly TableNameOrdinalComparer Instance = new TableNameOrdinalComparer();

            public int Compare(DataTableManifestEntry left, DataTableManifestEntry right)
            {
                return string.CompareOrdinal(left.TableName, right.TableName);
            }
        }
    }
}
