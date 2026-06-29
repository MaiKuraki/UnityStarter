using System;
using CycloneGames.Networking.Replication;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Low-allocation GAS replication planner that reuses Cyclone networking interest and send-budget logic.
    /// Instances keep scratch buffers and are not thread-safe; use one planner per simulation worker.
    /// </summary>
    public sealed class GASReplicationPlanner
    {
        private readonly NetworkReplicationPlanner _planner;
        private NetworkReplicatedObject[] _objectBuffer;
        private NetworkReplicationSelection[] _selectionBuffer;

        public GASReplicationPlanner()
            : this(new NetworkReplicationPlanner())
        {
        }

        public GASReplicationPlanner(NetworkReplicationPlanner planner)
        {
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _objectBuffer = Array.Empty<NetworkReplicatedObject>();
            _selectionBuffer = Array.Empty<NetworkReplicationSelection>();
        }

        public void Reserve(int sourceCapacity, int selectionCapacity)
        {
            if (sourceCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceCapacity));
            }

            if (selectionCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(selectionCapacity));
            }

            EnsureObjectCapacity(sourceCapacity);
            EnsureSelectionCapacity(selectionCapacity);
        }

        public int BuildPlan(
            in NetworkReplicationObserver observer,
            ReadOnlySpan<GASReplicationSource> sources,
            int serverTick,
            ref NetworkSendBudget budget,
            Span<GASReplicationSelection> results)
        {
            if (sources.Length == 0 || results.Length == 0)
            {
                return 0;
            }

            EnsureObjectCapacity(sources.Length);
            EnsureSelectionCapacity(results.Length);

            for (int i = 0; i < sources.Length; i++)
            {
                _objectBuffer[i] = sources[i].ToReplicatedObject();
            }

            int count = _planner.BuildPlan(
                observer,
                new ReadOnlySpan<NetworkReplicatedObject>(_objectBuffer, 0, sources.Length),
                serverTick,
                ref budget,
                new Span<NetworkReplicationSelection>(_selectionBuffer, 0, results.Length));

            for (int i = 0; i < count; i++)
            {
                NetworkReplicationSelection selection = _selectionBuffer[i];
                results[i] = new GASReplicationSelection(sources[selection.SourceIndex], selection);
            }

            return count;
        }

        private void EnsureObjectCapacity(int capacity)
        {
            if (_objectBuffer.Length < capacity)
            {
                Array.Resize(ref _objectBuffer, capacity);
            }
        }

        private void EnsureSelectionCapacity(int capacity)
        {
            if (_selectionBuffer.Length < capacity)
            {
                Array.Resize(ref _selectionBuffer, capacity);
            }
        }
    }
}
