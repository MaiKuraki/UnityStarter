using System;
using Luban;

namespace CycloneGames.DataTable.Unity.Integrations.Luban
{
    public static class LubanDataTableSetFactory
    {
        public static TTableSet Create<TTableSet>(
            IDataTableBytesProvider bytesProvider,
            Func<Func<string, ByteBuf>, TTableSet> factory)
        {
            return Create(bytesProvider, factory, DataTableLoadLimits.Default);
        }

        public static TTableSet Create<TTableSet>(
            IDataTableBytesProvider bytesProvider,
            Func<Func<string, ByteBuf>, TTableSet> factory,
            DataTableLoadLimits limits)
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
            int ownerThreadId = Environment.CurrentManagedThreadId;
            bool acceptsPayloadRequests = true;
            int payloadCount = 0;
            long totalPayloadBytes = 0;
            try
            {
                return factory(tableName =>
                {
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
                    return ByteBuf.Wrap(bytes.ToArray());
                });
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
            string tableName)
        {
            return CreateOwnedByteBuf(bytesProvider, tableName, DataTableLoadLimits.Default);
        }

        public static ByteBuf CreateOwnedByteBuf(
            IDataTableBytesProvider bytesProvider,
            string tableName,
            DataTableLoadLimits limits)
        {
            if (bytesProvider == null)
            {
                throw new ArgumentNullException(nameof(bytesProvider));
            }

            ReadOnlyMemory<byte> bytes = GetValidatedPayload(bytesProvider, tableName, limits);

            return ByteBuf.Wrap(bytes.ToArray());
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

            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName);
            limits.EnsureValid(nameof(limits));
            limits.ValidateTableName(normalizedName);

            ReadOnlyMemory<byte> bytes = bytesProvider.GetBytes(normalizedName);
            limits.ValidatePayloadLength(normalizedName, bytes.Length);
            limits.ValidateTotalBytes(bytes.Length);
            return bytes;
        }
    }
}
