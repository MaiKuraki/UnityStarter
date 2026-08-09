using System;
using System.Threading;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// A long-lived, allocation-free steady-state reader registered with one
    /// <see cref="DataTableStore"/>. Register readers once per subsystem, worker, or request scope.
    /// Publication never changes a reader implicitly; call <see cref="Refresh"/> only at a safe
    /// point where no operation can still use the reader's current snapshot.
    /// </summary>
    /// <remarks>
    /// Store publication and reads may run concurrently. Refresh and Dispose are linearized by the
    /// store, but they must not race with reads performed through the same reader. Separate
    /// execution contexts should use separate registered readers.
    /// </remarks>
    public sealed class DataTableReader : IDisposable
    {
        private DataTableStore _store;
        private DataTableStore.GenerationState _generationState;

        internal DataTableReader(
            DataTableStore store,
            DataTableStore.GenerationState generationState)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _generationState = generationState ?? throw new ArgumentNullException(nameof(generationState));
        }

        public bool IsDisposed => Volatile.Read(ref _store) == null;

        public DataTableSnapshot Snapshot
        {
            get
            {
                DataTableStore.GenerationState state = GetGenerationState();
                return state.Snapshot;
            }
        }

        public long Generation => GetGenerationState().Snapshot.Generation;

        public DataTableRevision Revision => GetGenerationState().Snapshot.Revision;

        public bool IsInitialized => GetGenerationState().Snapshot.IsInitialized;

        public TTable Get<TTable>() where TTable : class
        {
            return GetGenerationState().Snapshot.Catalog.Get<TTable>();
        }

        public bool TryGet<TTable>(out TTable table) where TTable : class
        {
            return GetGenerationState().Snapshot.Catalog.TryGet(out table);
        }

        /// <summary>
        /// Switches this reader to the store's latest generation. Returns false when it is already
        /// current. The caller must ensure this reader has no in-flight operations at this point.
        /// </summary>
        public bool Refresh()
        {
            DataTableStore store = Volatile.Read(ref _store);
            if (store == null)
            {
                throw new ObjectDisposedException(nameof(DataTableReader));
            }

            return store.RefreshReader(this);
        }

        public void Dispose()
        {
            DataTableStore store = Volatile.Read(ref _store);
            store?.ReleaseReader(this);
        }

        internal DataTableStore StoreUnsafe => _store;

        internal DataTableStore.GenerationState GenerationStateUnsafe => _generationState;

        internal void SetGenerationStateUnsafe(DataTableStore.GenerationState state)
        {
            Volatile.Write(ref _generationState, state);
        }

        internal void MarkDisposedUnsafe()
        {
            Volatile.Write(ref _generationState, null);
            Volatile.Write(ref _store, null);
        }

        private DataTableStore.GenerationState GetGenerationState()
        {
            DataTableStore.GenerationState state = Volatile.Read(ref _generationState);
            if (state == null)
            {
                throw new ObjectDisposedException(nameof(DataTableReader));
            }

            return state;
        }
    }
}
