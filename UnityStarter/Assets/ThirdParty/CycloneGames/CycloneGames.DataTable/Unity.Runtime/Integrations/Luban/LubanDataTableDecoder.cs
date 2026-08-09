using System;
using System.Threading;
using Luban;

namespace CycloneGames.DataTable.Unity.Integrations.Luban
{
    public static class LubanDataTableDecoder
    {
        public static TTableSet Decode<TTableSet>(
            IDataTableBytesProvider bytesProvider,
            Func<Func<string, ByteBuf>, TTableSet> factory,
            DataTableLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            if (bytesProvider == null)
            {
                throw new ArgumentNullException(nameof(bytesProvider));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            limits.EnsureValid(nameof(limits));
            cancellationToken.ThrowIfCancellationRequested();
            int ownerThreadId = Environment.CurrentManagedThreadId;
            bool acceptsPayloadRequests = true;
            int payloadCount = 0;
            long totalPayloadBytes = 0;
            try
            {
                TTableSet tableSet = factory(tableName =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Environment.CurrentManagedThreadId != ownerThreadId)
                    {
                        throw new InvalidOperationException(
                            "Luban payload requests must run synchronously on the factory owner thread.");
                    }

                    if (!acceptsPayloadRequests)
                    {
                        throw new InvalidOperationException(
                            "The Luban payload callback cannot be used after the table-set factory returns.");
                    }

                    ReadOnlyMemory<byte> bytes = GetValidatedPayload(bytesProvider, tableName, limits);
                    payloadCount = checked(payloadCount + 1);
                    totalPayloadBytes = checked(totalPayloadBytes + bytes.Length);
                    limits.ValidateTableCount(payloadCount);
                    limits.ValidateTotalBytes(totalPayloadBytes);
                    byte[] ownedBytes = bytes.ToArray();
                    cancellationToken.ThrowIfCancellationRequested();
                    return ByteBuf.Wrap(ownedBytes);
                });
                cancellationToken.ThrowIfCancellationRequested();
                return tableSet;
            }
            finally
            {
                acceptsPayloadRequests = false;
            }
        }

        /// <summary>
        /// Creates a Luban buffer backed by a private array copy. Use this safe default when
        /// the generated table may retain the buffer or provider lifetime is not tightly scoped.
        /// </summary>
        public static ByteBuf CreateOwnedByteBuf(
            IDataTableBytesProvider bytesProvider,
            string tableName,
            DataTableLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            if (bytesProvider == null)
            {
                throw new ArgumentNullException(nameof(bytesProvider));
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyMemory<byte> bytes = GetValidatedPayload(bytesProvider, tableName, limits);

            byte[] ownedBytes = bytes.ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return ByteBuf.Wrap(ownedBytes);
        }

        private static ReadOnlyMemory<byte> GetValidatedPayload(
            IDataTableBytesProvider bytesProvider,
            string tableName,
            DataTableLoadLimits limits)
        {
            if (bytesProvider == null)
            {
                throw new ArgumentNullException(nameof(bytesProvider));
            }

            limits.EnsureValid(nameof(limits));
            string normalizedName = limits.NormalizeTableName(tableName);

            ReadOnlyMemory<byte> bytes = bytesProvider.GetBytes(normalizedName);
            limits.ValidatePayloadLength(normalizedName, bytes.Length);
            limits.ValidateTotalBytes(bytes.Length);
            return bytes;
        }
    }
}
