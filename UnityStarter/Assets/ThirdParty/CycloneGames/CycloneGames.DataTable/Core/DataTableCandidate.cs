using System;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// A validated, unpublished table generation. The caller owns the candidate and its resource
    /// owner until <see cref="DataTableStore.TryPublish(DataTableCandidate, long)"/> succeeds.
    /// A successful publication consumes the candidate and transfers exclusive ownership to the
    /// store. A stale or otherwise rejected publication leaves ownership with the caller.
    /// </summary>
    public sealed class DataTableCandidate : IDisposable
    {
        private const int StateCallerOwned = 0;
        private const int StateStoreOwned = 1;
        private const int StateDisposing = 2;
        private const int StateDisposeFailed = 3;
        private const int StateDisposed = 4;

        private readonly object _syncRoot = new object();
        private DataTableCatalog _catalog;
        private DataTableRevision _revision;
        private IDisposable _resourceOwner;
        private int _state;

        /// <param name="resourceOwner">
        /// Optional exclusive owner of backing resources. After publication it may be disposed on
        /// the thread that releases the generation's final reader. Thread-affine resources must be
        /// wrapped in an owner-thread dispatch adapter before being supplied here. Owners that can
        /// throw from Dispose must tolerate a later retry.
        /// </param>
        public DataTableCandidate(
            DataTableCatalog catalog,
            DataTableRevision revision,
            IDisposable resourceOwner = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (!revision.IsPublishable)
            {
                throw new ArgumentException(
                    "A candidate requires a publishable data-table revision.",
                    nameof(revision));
            }

            _revision = revision;
            _resourceOwner = resourceOwner;
        }

        /// <summary>
        /// Gets whether the candidate can still be published or explicitly disposed by its caller.
        /// </summary>
        public bool IsCallerOwned
        {
            get
            {
                lock (_syncRoot)
                {
                    return _state == StateCallerOwned;
                }
            }
        }

        public bool IsCommitted
        {
            get
            {
                lock (_syncRoot)
                {
                    return _state == StateStoreOwned;
                }
            }
        }

        public bool IsDisposed
        {
            get
            {
                lock (_syncRoot)
                {
                    return _state == StateDisposed;
                }
            }
        }

        /// <summary>
        /// Gets whether the caller-owned resource owner threw during disposal and is retained for
        /// another explicit Dispose attempt.
        /// </summary>
        public bool HasDisposeFailure
        {
            get
            {
                lock (_syncRoot)
                {
                    return _state == StateDisposeFailed;
                }
            }
        }

        public DataTableCatalog Catalog
        {
            get
            {
                lock (_syncRoot)
                {
                    ThrowIfNotCallerOwned();
                    return _catalog;
                }
            }
        }

        public DataTableRevision Revision
        {
            get
            {
                lock (_syncRoot)
                {
                    ThrowIfNotCallerOwned();
                    return _revision;
                }
            }
        }

        public bool OwnsResources
        {
            get
            {
                lock (_syncRoot)
                {
                    return _resourceOwner != null &&
                        (_state == StateCallerOwned ||
                         _state == StateDisposing ||
                         _state == StateDisposeFailed);
                }
            }
        }

        public void Dispose()
        {
            IDisposable resourceOwner;
            lock (_syncRoot)
            {
                if (_state == StateCallerOwned)
                {
                    resourceOwner = _resourceOwner;
                    _catalog = null;
                    _revision = default;
                    if (resourceOwner == null)
                    {
                        _state = StateDisposed;
                        return;
                    }

                    _state = StateDisposing;
                }
                else if (_state == StateDisposeFailed)
                {
                    resourceOwner = _resourceOwner;
                    _state = StateDisposing;
                }
                else
                {
                    return;
                }
            }

            try
            {
                resourceOwner.Dispose();
            }
            catch
            {
                lock (_syncRoot)
                {
                    _state = StateDisposeFailed;
                }

                throw;
            }

            lock (_syncRoot)
            {
                _resourceOwner = null;
                _state = StateDisposed;
            }
        }

        internal object SyncRoot => _syncRoot;

        internal void GetCallerOwnedStateUnsafe(
            out DataTableCatalog catalog,
            out DataTableRevision revision,
            out IDisposable resourceOwner)
        {
            ThrowIfNotCallerOwned();
            catalog = _catalog;
            revision = _revision;
            resourceOwner = _resourceOwner;
        }

        internal void MarkStoreOwnedUnsafe()
        {
            ThrowIfNotCallerOwned();
            _state = StateStoreOwned;
            ClearTransferredReferences();
        }

        private void ClearTransferredReferences()
        {
            _catalog = null;
            _revision = default;
            _resourceOwner = null;
        }

        private void ThrowIfNotCallerOwned()
        {
            if (_state == StateDisposed)
            {
                throw new ObjectDisposedException(nameof(DataTableCandidate));
            }

            if (_state == StateStoreOwned)
            {
                throw new InvalidOperationException("The data-table candidate has already been published.");
            }

            if (_state == StateDisposing || _state == StateDisposeFailed)
            {
                throw new InvalidOperationException(
                    "The data-table candidate is disposing or retained after a disposal failure.");
            }
        }
    }
}
