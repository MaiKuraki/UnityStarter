using System;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Explicit allocation limits for untrusted or remotely supplied table payloads. The defaults
    /// are broad fail-fast guardrails, not safe memory budgets or platform performance targets;
    /// products should set tighter values from measured content profiles.
    /// </summary>
    public readonly struct DataTableLoadLimits : IEquatable<DataTableLoadLimits>
    {
        public const int DEFAULT_MAX_TABLE_COUNT = 4096;
        public const int DEFAULT_MAX_BYTES_PER_TABLE = 64 * 1024 * 1024;
        public const long DEFAULT_MAX_TOTAL_BYTES = 512L * 1024L * 1024L;
        public const int DEFAULT_MAX_ROWS_PER_TABLE = 2_000_000;
        public const int DEFAULT_MAX_TABLE_NAME_LENGTH = 256;

        public static readonly DataTableLoadLimits Default = new DataTableLoadLimits(
            DEFAULT_MAX_TABLE_COUNT,
            DEFAULT_MAX_BYTES_PER_TABLE,
            DEFAULT_MAX_TOTAL_BYTES,
            DEFAULT_MAX_ROWS_PER_TABLE,
            DEFAULT_MAX_TABLE_NAME_LENGTH);

        public DataTableLoadLimits(int maxTableCount, int maxBytesPerTable, long maxTotalBytes)
            : this(
                maxTableCount,
                maxBytesPerTable,
                maxTotalBytes,
                DEFAULT_MAX_ROWS_PER_TABLE,
                DEFAULT_MAX_TABLE_NAME_LENGTH)
        {
        }

        public DataTableLoadLimits(
            int maxTableCount,
            int maxBytesPerTable,
            long maxTotalBytes,
            int maxRowsPerTable,
            int maxTableNameLength)
        {
            if (maxTableCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxTableCount),
                    maxTableCount,
                    "Maximum table count must be greater than zero.");
            }

            if (maxBytesPerTable <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxBytesPerTable),
                    maxBytesPerTable,
                    "Maximum bytes per table must be greater than zero.");
            }

            if (maxTotalBytes < maxBytesPerTable)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxTotalBytes),
                    maxTotalBytes,
                    "Maximum total bytes must be at least the per-table limit.");
            }

            if (maxRowsPerTable <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRowsPerTable),
                    maxRowsPerTable,
                    "Maximum rows per table must be greater than zero.");
            }

            if (maxTableNameLength <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxTableNameLength),
                    maxTableNameLength,
                    "Maximum table-name length must be greater than zero.");
            }

            MaxTableCount = maxTableCount;
            MaxBytesPerTable = maxBytesPerTable;
            MaxTotalBytes = maxTotalBytes;
            MaxRowsPerTable = maxRowsPerTable;
            MaxTableNameLength = maxTableNameLength;
        }

        public int MaxTableCount { get; }

        public int MaxBytesPerTable { get; }

        public long MaxTotalBytes { get; }

        public int MaxRowsPerTable { get; }

        public int MaxTableNameLength { get; }

        public bool IsValid =>
            MaxTableCount > 0 &&
            MaxBytesPerTable > 0 &&
            MaxTotalBytes >= MaxBytesPerTable &&
            MaxRowsPerTable > 0 &&
            MaxTableNameLength > 0;

        public void EnsureValid(string parameterName = null)
        {
            if (!IsValid)
            {
                throw new ArgumentException(
                    "Data-table load limits are not initialized or contain invalid values.",
                    parameterName ?? "limits");
            }
        }

        public void ValidateTableCount(int tableCount)
        {
            EnsureValid();
            if (tableCount < 0 || tableCount > MaxTableCount)
            {
                throw new InvalidOperationException(
                    $"Data-table count exceeds the configured limit. Count={tableCount}, Limit={MaxTableCount}.");
            }
        }

        public void ValidatePayloadLength(string tableName, int byteLength)
        {
            EnsureValid();
            if (byteLength <= 0)
            {
                throw new InvalidOperationException(
                    $"Data-table payload is empty. Table={tableName ?? "<unknown>"}.");
            }

            if (byteLength > MaxBytesPerTable)
            {
                throw new InvalidOperationException(
                    $"Data-table payload exceeds the per-table limit. Table={tableName ?? "<unknown>"}, Bytes={byteLength}, Limit={MaxBytesPerTable}.");
            }
        }

        public void ValidateRowCount(string tableName, int rowCount)
        {
            EnsureValid();
            if (rowCount < 0 || rowCount > MaxRowsPerTable)
            {
                throw new InvalidOperationException(
                    $"Data-table row count exceeds the configured limit. Table={tableName ?? "<unknown>"}, Rows={rowCount}, Limit={MaxRowsPerTable}.");
            }
        }

        public void ValidateTableName(string tableName)
        {
            EnsureValid();
            if (string.IsNullOrEmpty(tableName))
            {
                throw new InvalidOperationException("Data-table name is empty.");
            }

            if (tableName.Length > MaxTableNameLength)
            {
                throw new InvalidOperationException(
                    $"Data-table name exceeds the configured limit. Length={tableName.Length}, Limit={MaxTableNameLength}.");
            }
        }

        public void ValidateTotalBytes(long totalBytes)
        {
            EnsureValid();
            if (totalBytes < 0 || totalBytes > MaxTotalBytes)
            {
                throw new InvalidOperationException(
                    $"Data-table payloads exceed the total-byte limit. Bytes={totalBytes}, Limit={MaxTotalBytes}.");
            }
        }

        public bool Equals(DataTableLoadLimits other)
        {
            return MaxTableCount == other.MaxTableCount &&
                   MaxBytesPerTable == other.MaxBytesPerTable &&
                   MaxTotalBytes == other.MaxTotalBytes &&
                   MaxRowsPerTable == other.MaxRowsPerTable &&
                   MaxTableNameLength == other.MaxTableNameLength;
        }

        public override bool Equals(object obj)
        {
            return obj is DataTableLoadLimits other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = MaxTableCount;
                hashCode = (hashCode * 397) ^ MaxBytesPerTable;
                hashCode = (hashCode * 397) ^ MaxTotalBytes.GetHashCode();
                hashCode = (hashCode * 397) ^ MaxRowsPerTable;
                hashCode = (hashCode * 397) ^ MaxTableNameLength;
                return hashCode;
            }
        }

        public static bool operator ==(DataTableLoadLimits left, DataTableLoadLimits right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DataTableLoadLimits left, DataTableLoadLimits right)
        {
            return !left.Equals(right);
        }
    }
}
