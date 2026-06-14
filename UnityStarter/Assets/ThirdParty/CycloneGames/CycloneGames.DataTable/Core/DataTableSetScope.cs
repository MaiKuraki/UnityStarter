using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    public sealed class DataTableSetScope : IDisposable
    {
        private readonly Dictionary<Type, object> _tables;
        private readonly IDisposable _resourceOwner;
        private bool _disposed;

        public DataTableSetScope(
            object root,
            Dictionary<Type, object> tables)
            : this(root, tables, null)
        {
        }

        public DataTableSetScope(
            object root,
            Dictionary<Type, object> tables,
            IDisposable resourceOwner)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _tables = tables ?? throw new ArgumentNullException(nameof(tables));
            _resourceOwner = resourceOwner;
        }

        public object Root { get; private set; }

        public TTable Get<TTable>() where TTable : class
        {
            ThrowIfDisposed();

            if (_tables.TryGetValue(typeof(TTable), out object table))
            {
                return (TTable)table;
            }

            throw new KeyNotFoundException($"Data table scope does not contain table: {typeof(TTable).FullName}");
        }

        public bool TryGet<TTable>(out TTable table) where TTable : class
        {
            ThrowIfDisposed();

            if (_tables.TryGetValue(typeof(TTable), out object value))
            {
                table = (TTable)value;
                return true;
            }

            table = null;
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Root = null;
            _tables.Clear();
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
