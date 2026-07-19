using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MessagePack;

namespace CycloneGames.DataTable.Unity.Integrations.MessagePack
{
    /// <summary>
    /// Decodes bounded MessagePack row arrays into catalog candidates. Loading, catalog publication,
    /// resolver selection, and the trust policy remain explicit caller responsibilities.
    /// </summary>
    public static class MessagePackConfigProvider
    {
        /// <summary>
        /// Deserializes a MessagePack array payload without modifying the active registry.
        /// Pass source-generated resolver options for IL2CPP/AOT and an explicit security policy.
        /// </summary>
        public static DataTable<TRow> Build<TRow>(
            ReadOnlyMemory<byte> bytes,
            MessagePackSerializerOptions options,
            MessagePackSecurity security,
            DataTableLoadLimits limits,
            CancellationToken cancellationToken = default)
            where TRow : IDataRow
        {
            TRow[] rows = DeserializeArray<TRow>(
                bytes,
                options,
                security,
                limits,
                cancellationToken);
            return BuildRows(rows, limits);
        }

        /// <summary>
        /// Deserializes rows that cannot or should not implement a framework interface. The key
        /// selector runs once per row while the immutable lookup index is built.
        /// </summary>
        public static DataTable<TKey, TRow> Build<TKey, TRow>(
            ReadOnlyMemory<byte> bytes,
            Func<TRow, TKey> keySelector,
            MessagePackSerializerOptions options,
            MessagePackSecurity security,
            DataTableLoadLimits limits,
            IEqualityComparer<TKey> comparer = null,
            CancellationToken cancellationToken = default)
        {
            ValidateKeySelector(keySelector);
            TRow[] rows = DeserializeArray<TRow>(bytes, options, security, limits, cancellationToken);
            return BuildRows(rows, keySelector, limits, comparer);
        }

        /// <summary>
        /// Wraps an already materialized, caller-transferred row array as a table candidate.
        /// The array must not be mutated after this call.
        /// </summary>
        public static DataTable<TRow> BuildRows<TRow>(
            TRow[] ownedRows,
            DataTableLoadLimits limits)
            where TRow : IDataRow
        {
            ValidateOwnedRows(ownedRows, limits);
            if (ownedRows.Length == 0)
            {
                DataTableLogger.LogWarning(
                    $"Decoded DataTable<{typeof(TRow).Name}> is empty.");
            }

            DataTable<TRow> table = DataTable<TRow>.FromOwnedArray(ownedRows, limits);
            DataTableLogger.LogInfo(
                $"Built DataTable<{typeof(TRow).Name}> candidate ({table.Count} rows).");
            return table;
        }

        /// <summary>
        /// Wraps arbitrary generated rows, including value-type rows, using an explicit stable key.
        /// </summary>
        public static DataTable<TKey, TRow> BuildRows<TKey, TRow>(
            TRow[] ownedRows,
            Func<TRow, TKey> keySelector,
            DataTableLoadLimits limits,
            IEqualityComparer<TKey> comparer = null)
        {
            ValidateKeySelector(keySelector);
            ValidateOwnedRows(ownedRows, limits);

            if (ownedRows.Length == 0)
            {
                DataTableLogger.LogWarning(
                    $"Decoded DataTable<{typeof(TRow).Name}> is empty.");
            }

            DataTable<TKey, TRow> table = DataTable<TKey, TRow>.FromOwnedArray(
                ownedRows,
                keySelector,
                limits,
                comparer);
            DataTableLogger.LogInfo(
                $"Built DataTable<{typeof(TKey).Name}, {typeof(TRow).Name}> candidate ({table.Count} rows).");
            return table;
        }

        private static void ValidateArguments<TRow>(
            ReadOnlyMemory<byte> bytes,
            MessagePackSerializerOptions options,
            MessagePackSecurity security,
            DataTableLoadLimits limits)
        {
            if (bytes.IsEmpty)
            {
                throw new ArgumentException(
                    $"Bytes for DataTable<{typeof(TRow).Name}> are empty.",
                    nameof(bytes));
            }

            if (options == null)
            {
                throw new ArgumentNullException(
                    nameof(options),
                    "MessagePack options must explicitly select the project's generated resolver.");
            }

            if (security == null)
            {
                throw new ArgumentNullException(nameof(security));
            }

            limits.EnsureValid(nameof(limits));
            if (!security.HashCollisionResistant ||
                security.MaximumObjectGraphDepth <= 0 ||
                security.MaximumDecompressedSize <= 0 ||
                security.MaximumDecompressedSize > limits.MaxBytesPerTable)
            {
                throw new ArgumentException(
                    "MessagePack security must be based on MessagePackSecurity.UntrustedData, " +
                    "use collision-resistant hashing, and bound decompressed bytes to the DataTable per-table limit.",
                    nameof(security));
            }

            limits.ValidatePayloadLength(typeof(TRow).FullName, bytes.Length);
            limits.ValidateTotalBytes(bytes.Length);
        }

        private static TRow[] DeserializeArray<TRow>(
            ReadOnlyMemory<byte> bytes,
            MessagePackSerializerOptions options,
            MessagePackSecurity security,
            DataTableLoadLimits limits,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateArguments<TRow>(bytes, options, security, limits);
            ValidateArrayHeader(bytes, limits.MaxRowsPerTable);

            try
            {
                TRow[] rows = MessagePackSerializer.Deserialize<TRow[]>(
                                  bytes,
                                  options.WithSecurity(security),
                                  out int bytesRead,
                                  cancellationToken) ??
                              Array.Empty<TRow>();
                if (bytesRead != bytes.Length)
                {
                    throw new InvalidDataException(
                        $"MessagePack DataTable payload contains {bytes.Length - bytesRead} trailing bytes.");
                }

                return rows;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                throw new InvalidDataException(
                    $"Failed to deserialize MessagePack data for {typeof(TRow).Name}. " +
                    "The payload must be a TRow[] and options must include an AOT-safe project resolver.",
                    exception);
            }
        }

        private static void ValidateOwnedRows<TRow>(TRow[] ownedRows, DataTableLoadLimits limits)
        {
            if (ownedRows == null)
            {
                throw new ArgumentNullException(nameof(ownedRows));
            }

            limits.EnsureValid(nameof(limits));
            limits.ValidateRowCount(typeof(TRow).FullName, ownedRows.Length);
        }

        private static void ValidateKeySelector<TKey, TRow>(Func<TRow, TKey> keySelector)
        {
            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }
        }

        private static void ValidateArrayHeader(
            ReadOnlyMemory<byte> bytes,
            int maxRowCount)
        {
            try
            {
                var reader = new MessagePackReader(bytes);
                int rowCount = reader.ReadArrayHeader();
                if (rowCount > maxRowCount)
                {
                    throw new InvalidDataException(
                        $"MessagePack payload declares {rowCount} rows; the configured limit is {maxRowCount}.");
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                throw new InvalidDataException(
                    "MessagePack DataTable payload must start with a bounded array header.",
                    exception);
            }
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException &&
                   exception is not StackOverflowException &&
                   exception is not AccessViolationException &&
                   exception is not ThreadAbortException;
        }
    }
}
