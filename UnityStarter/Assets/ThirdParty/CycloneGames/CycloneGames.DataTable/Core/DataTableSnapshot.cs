using System;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Immutable identity and catalog for one published generation. A snapshot obtained from a
    /// <see cref="DataTableReader"/> is borrowed: it remains valid until that reader is refreshed
    /// or disposed. Do not retain it beyond the reader's refresh safe point.
    /// </summary>
    public sealed class DataTableSnapshot
    {
        internal static readonly DataTableSnapshot Initial = new DataTableSnapshot(
            DataTableCatalog.Empty,
            generation: 0,
            revision: DataTableRevision.None,
            isInitialized: false);

        internal DataTableSnapshot(
            DataTableCatalog catalog,
            long generation,
            DataTableRevision revision,
            bool isInitialized)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (generation < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            Generation = generation;
            Revision = revision;
            IsInitialized = isInitialized;
        }

        public DataTableCatalog Catalog { get; }

        public long Generation { get; }

        public DataTableRevision Revision { get; }

        public bool IsInitialized { get; }

        public TTable Get<TTable>() where TTable : class
        {
            return Catalog.Get<TTable>();
        }

        public bool TryGet<TTable>(out TTable table) where TTable : class
        {
            return Catalog.TryGet(out table);
        }
    }
}
