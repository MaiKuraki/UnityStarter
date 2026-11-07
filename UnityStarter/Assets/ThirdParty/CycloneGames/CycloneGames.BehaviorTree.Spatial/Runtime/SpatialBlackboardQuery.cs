using System;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.Spatial.Core;

namespace CycloneGames.BehaviorTree.Runtime.Integrations.Spatial
{
    /// <summary>
    /// Runs a nearest-neighbour query against a spatial index and publishes the result into
    /// blackboard slots.
    /// </summary>
    /// <remarks>
    /// Queries stay outside the tree: the game layer calls <see cref="QueryNearest(float,float,float,float,uint)"/>
    /// once per agent per update, then <see cref="PublishNearest"/> writes visibility and squared distance into
    /// the blackboard. Conditions and decorators read those slots through the ordinary blackboard API, so the
    /// tree never holds an index reference and the same tree runs on a Unity client and on a headless server.
    ///
    /// Buffers are allocated in the constructor; query and publish allocate nothing. Writes go through the
    /// blackboard's change detection, so republishing identical values does not notify observers and does not
    /// enter the network delta. Not thread-safe: use one instance per writer thread.
    /// </remarks>
    public sealed class SpatialBlackboardQuery<TItem> : IDisposable
    {
        private static readonly TItem[] NoItems = Array.Empty<TItem>();
        private static readonly float[] NoDistances = Array.Empty<float>();

        private readonly ISpatialReadOnlyIndex<TItem> _index;
        private readonly int _maxResults;
        private TItem[] _items;
        private float[] _squaredDistances;
        private int _count;
        private bool _disposed;

        /// <param name="index">Grid or snapshot used for the query. The caller owns its lifetime.</param>
        /// <param name="maxResults">Upper bound on results per query; fixed for the lifetime of the instance.</param>
        public SpatialBlackboardQuery(ISpatialReadOnlyIndex<TItem> index, int maxResults = 8)
        {
            if (index == null)
            {
                throw new ArgumentNullException(nameof(index));
            }

            if (maxResults < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults, "At least one result must be allowed.");
            }

            _index = index;
            _maxResults = maxResults;
            _items = new TItem[maxResults];
            _squaredDistances = new float[maxResults];
        }

        public ISpatialReadOnlyIndex<TItem> Index => _index;

        public int MaxResults => _maxResults;

        /// <summary>Results written by the last query.</summary>
        public int Count => _count;

        /// <summary>Closest item of the last query, or <c>default</c> when nothing matched.</summary>
        public TItem NearestItem => _count > 0 ? _items[0] : default;

        /// <summary>
        /// Squared distance of the closest item, in the unit the index reports. Equal to
        /// <c>distance * distance</c>; take the square root only when a real distance is required.
        /// </summary>
        public float NearestSquaredDistance => _count > 0 ? _squaredDistances[0] : 0f;

        /// <summary>Valid results of the last query, closest first.</summary>
        public ReadOnlySpan<TItem> Results => new ReadOnlySpan<TItem>(_items, 0, _count);

        /// <summary>Squared distances aligned with <see cref="Results"/>.</summary>
        public ReadOnlySpan<float> SquaredDistances => new ReadOnlySpan<float>(_squaredDistances, 0, _count);

        /// <summary>
        /// Fills the result buffers with the closest elements inside the radius, ascending by distance.
        /// A non-positive radius clears the result rather than calling the index.
        /// </summary>
        /// <returns>Number of results written.</returns>
        public int QueryNearest(float x, float y, float z, float radius, uint layerMask)
        {
            ThrowIfDisposed();

            if (!(radius > 0f))
            {
                _count = 0;
                return 0;
            }

            _count = _index.QueryNearest(x, y, z, radius, _maxResults, layerMask, _items, _squaredDistances);
            return _count;
        }

        /// <inheritdoc cref="QueryNearest(float,float,float,float,uint)"/>
        public int QueryNearest(in BTFloat3 origin, float radius, uint layerMask)
        {
            return QueryNearest(origin.X, origin.Y, origin.Z, radius, layerMask);
        }

        /// <summary>
        /// Reads a <see cref="BTFloat3"/> slot as the origin and queries from it. Missing or
        /// mistyped slots yield a zero origin rather than an exception.
        /// </summary>
        public int QueryNearestFrom(RuntimeBlackboard blackboard, int originKey, float radius, uint layerMask)
        {
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            return QueryNearest(blackboard.GetFloat3(originKey, BTFloat3.Zero), radius, layerMask);
        }

        /// <summary>
        /// Writes the outcome of the last query: visibility as a bool and squared distance as a float.
        /// The distance slot receives zero when nothing matched, so read visibility first.
        /// </summary>
        public void PublishNearest(RuntimeBlackboard blackboard, int visibleKey, int squaredDistanceKey)
        {
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            ThrowIfDisposed();

            blackboard.SetBool(visibleKey, _count > 0);
            blackboard.SetFloat(squaredDistanceKey, NearestSquaredDistance);
        }

        /// <summary>Writes the result count of the last query as an int slot.</summary>
        public void PublishCount(RuntimeBlackboard blackboard, int countKey)
        {
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            ThrowIfDisposed();

            blackboard.SetInt(countKey, _count);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _count = 0;
            _items = NoItems;
            _squaredDistances = NoDistances;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SpatialBlackboardQuery<TItem>));
            }
        }
    }
}
