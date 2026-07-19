using System;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Explicit lifetime owner for a generated table-set root, an immutable catalog, and an
    /// optional backing resource owner. Disposing the scope disposes only the supplied owner;
    /// table instances and catalogs do not have implicit ownership semantics.
    /// </summary>
    public sealed class DataTableSetScope : IDisposable
    {
        private readonly IDisposable _resourceOwner;
        private DataTableCatalog _catalog;
        private bool _disposed;

        public DataTableSetScope(object root, DataTableCatalog catalog)
            : this(root, catalog, resourceOwner: null)
        {
        }

        public DataTableSetScope(
            object root,
            DataTableCatalog catalog,
            IDisposable resourceOwner)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resourceOwner = resourceOwner;
        }

        public object Root { get; private set; }

        public DataTableCatalog Catalog
        {
            get
            {
                ThrowIfDisposed();
                return _catalog;
            }
        }

        public bool OwnsResources => _resourceOwner != null;

        public bool IsDisposed => _disposed;

        public TTable Get<TTable>() where TTable : class
        {
            ThrowIfDisposed();
            return _catalog.Get<TTable>();
        }

        public bool TryGet<TTable>(out TTable table) where TTable : class
        {
            ThrowIfDisposed();
            return _catalog.TryGet(out table);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Root = null;
            _catalog = DataTableCatalog.Empty;
            _resourceOwner?.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DataTableSetScope));
            }
        }
    }
}
