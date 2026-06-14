using System;

namespace CycloneGames.DataTable
{
    public readonly struct DataTableManifestEntry
    {
        public const int UNKNOWN_BYTE_LENGTH = -1;

        public readonly string TableName;
        public readonly string Location;
        public readonly int ExpectedByteLength;
        public readonly string Sha256Hex;
        public readonly bool Required;

        public DataTableManifestEntry(
            string tableName,
            string location = null,
            int expectedByteLength = UNKNOWN_BYTE_LENGTH,
            string sha256Hex = null,
            bool required = true)
        {
            string normalizedTableName = DataTableNameUtility.NormalizeTableName(tableName);
            if (string.IsNullOrEmpty(normalizedTableName))
            {
                throw new ArgumentException("Table name is null or empty.", nameof(tableName));
            }

            if (expectedByteLength < UNKNOWN_BYTE_LENGTH)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedByteLength),
                    expectedByteLength,
                    "Expected byte length must be -1 or greater.");
            }

            string normalizedSha256 = DataTableHashUtility.NormalizeSha256Hex(sha256Hex);
            if (!string.IsNullOrEmpty(normalizedSha256) &&
                !IsSha256Hex(normalizedSha256))
            {
                throw new ArgumentException(
                    "SHA-256 hash must be a 64-character hexadecimal string.",
                    nameof(sha256Hex));
            }

            TableName = normalizedTableName;
            Location = DataTableNameUtility.NormalizePath(location);
            ExpectedByteLength = expectedByteLength;
            Sha256Hex = normalizedSha256;
            Required = required;
        }

        public bool HasLocation => !string.IsNullOrEmpty(Location);

        public bool HasExpectedByteLength => ExpectedByteLength >= 0;

        public bool HasSha256Hash => !string.IsNullOrEmpty(Sha256Hex);

        private static bool IsSha256Hex(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isDigit = c >= '0' && c <= '9';
                bool isLowerHex = c >= 'a' && c <= 'f';
                if (!isDigit && !isLowerHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
