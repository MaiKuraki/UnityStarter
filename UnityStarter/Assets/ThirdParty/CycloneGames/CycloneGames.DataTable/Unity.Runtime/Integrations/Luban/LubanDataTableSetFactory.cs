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
            if (bytesProvider == null)
            {
                throw new ArgumentNullException(nameof(bytesProvider));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return factory(tableName => CreateByteBuf(bytesProvider, tableName));
        }

        public static ByteBuf CreateByteBuf(
            IDataTableBytesProvider bytesProvider,
            string tableName)
        {
            if (bytesProvider == null)
            {
                throw new ArgumentNullException(nameof(bytesProvider));
            }

            return ByteBuf.Wrap(bytesProvider.GetBytes(tableName));
        }
    }
}
