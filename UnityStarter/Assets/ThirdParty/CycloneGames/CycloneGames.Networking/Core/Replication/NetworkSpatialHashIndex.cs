using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Replication
{
    public enum NetworkSpatialAxisMode : byte
    {
        XZ = 0,
        XY = 1,
        XYZ = 2
    }

    public readonly struct NetworkSpatialQueryResult
    {
        public readonly ulong ObjectId;
        public readonly int SourceIndex;
        public readonly float SqrDistance;

        public NetworkSpatialQueryResult(ulong objectId, int sourceIndex, float sqrDistance)
        {
            ObjectId = objectId;
            SourceIndex = sourceIndex;
            SqrDistance = sqrDistance;
        }
    }

    public sealed class NetworkSpatialHashIndex
    {
        private readonly Dictionary<CellKey, List<Entry>> _cells;
        private readonly Dictionary<ulong, ObjectLocation> _locations;
        private readonly float _cellSize;
        private readonly NetworkSpatialAxisMode _axisMode;

        public NetworkSpatialHashIndex(
            float cellSize,
            NetworkSpatialAxisMode axisMode = NetworkSpatialAxisMode.XZ,
            int capacity = 1024)
        {
            if (cellSize <= 0f || !IsFinite(cellSize))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            }

            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _cellSize = cellSize;
            _axisMode = axisMode;
            _cells = new Dictionary<CellKey, List<Entry>>(capacity);
            _locations = new Dictionary<ulong, ObjectLocation>(capacity);
        }

        public int Count
        {
            get
            {
                return _locations.Count;
            }
        }

        public void Upsert(
            ulong objectId,
            int sourceIndex,
            NetworkVector3 position,
            uint layerMask = NetworkReplicationObserver.ALL_LAYERS)
        {
            if (objectId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(objectId));
            }

            if (sourceIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            }

            ValidateFinite(position, nameof(position));

            CellKey cell = GetCell(position);
            var entry = new Entry(objectId, sourceIndex, position, layerMask);

            if (_locations.TryGetValue(objectId, out ObjectLocation location))
            {
                if (location.Cell.Equals(cell))
                {
                    UpdateEntry(cell, entry);
                    _locations[objectId] = new ObjectLocation(cell);
                    return;
                }

                RemoveEntry(location.Cell, objectId);
            }

            AddEntry(cell, entry);
            _locations[objectId] = new ObjectLocation(cell);
        }

        public bool Remove(ulong objectId)
        {
            if (objectId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(objectId));
            }

            if (!_locations.TryGetValue(objectId, out ObjectLocation location))
            {
                return false;
            }

            _locations.Remove(objectId);
            RemoveEntry(location.Cell, objectId);
            return true;
        }

        public int Query(
            NetworkVector3 center,
            float radius,
            uint layerMask,
            Span<NetworkSpatialQueryResult> results)
        {
            if (radius < 0f || !IsFinite(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            ValidateFinite(center, nameof(center));

            if (results.Length == 0 || layerMask == 0u)
            {
                return 0;
            }

            CellKey min = GetCell(new NetworkVector3(center.X - radius, center.Y - radius, center.Z - radius));
            CellKey max = GetCell(new NetworkVector3(center.X + radius, center.Y + radius, center.Z + radius));
            float sqrRadius = radius * radius;
            int count = 0;

            for (int x = min.X; x <= max.X; x++)
            {
                for (int y = min.Y; y <= max.Y; y++)
                {
                    for (int z = min.Z; z <= max.Z; z++)
                    {
                        if (!_cells.TryGetValue(new CellKey(x, y, z), out List<Entry> entries))
                        {
                            continue;
                        }

                        for (int i = 0; i < entries.Count; i++)
                        {
                            Entry entry = entries[i];
                            if ((entry.LayerMask & layerMask) == 0u)
                            {
                                continue;
                            }

                            float sqrDistance = SqrDistance(center, entry.Position);
                            if (sqrDistance > sqrRadius || count >= results.Length)
                            {
                                continue;
                            }

                            results[count++] = new NetworkSpatialQueryResult(
                                entry.ObjectId,
                                entry.SourceIndex,
                                sqrDistance);
                        }
                    }
                }
            }

            return count;
        }

        public void Clear()
        {
            _cells.Clear();
            _locations.Clear();
        }

        private void AddEntry(in CellKey cell, in Entry entry)
        {
            if (!_cells.TryGetValue(cell, out List<Entry> entries))
            {
                entries = new List<Entry>(8);
                _cells.Add(cell, entries);
            }

            entries.Add(entry);
        }

        private void UpdateEntry(in CellKey cell, in Entry entry)
        {
            List<Entry> entries = _cells[cell];
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].ObjectId == entry.ObjectId)
                {
                    entries[i] = entry;
                    return;
                }
            }

            entries.Add(entry);
        }

        private void RemoveEntry(in CellKey cell, ulong objectId)
        {
            if (!_cells.TryGetValue(cell, out List<Entry> entries))
            {
                return;
            }

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].ObjectId != objectId)
                {
                    continue;
                }

                int lastIndex = entries.Count - 1;
                entries[i] = entries[lastIndex];
                entries.RemoveAt(lastIndex);
                break;
            }

            if (entries.Count == 0)
            {
                _cells.Remove(cell);
            }
        }

        private CellKey GetCell(NetworkVector3 position)
        {
            int x = ToCell(position.X);
            int y = _axisMode == NetworkSpatialAxisMode.XZ ? 0 : ToCell(position.Y);
            int z = _axisMode == NetworkSpatialAxisMode.XY ? 0 : ToCell(position.Z);
            return new CellKey(x, y, z);
        }

        private int ToCell(float value)
        {
            float scaled = value / _cellSize;
            if (scaled <= int.MinValue || scaled >= int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return (int)MathF.Floor(scaled);
        }

        private float SqrDistance(NetworkVector3 left, NetworkVector3 right)
        {
            float dx = left.X - right.X;
            float dy = _axisMode == NetworkSpatialAxisMode.XZ ? 0f : left.Y - right.Y;
            float dz = _axisMode == NetworkSpatialAxisMode.XY ? 0f : left.Z - right.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static void ValidateFinite(NetworkVector3 value, string parameterName)
        {
            if (!IsFinite(value.X) || !IsFinite(value.Y) || !IsFinite(value.Z))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct ObjectLocation
        {
            public readonly CellKey Cell;

            public ObjectLocation(CellKey cell)
            {
                Cell = cell;
            }
        }

        private readonly struct Entry
        {
            public readonly ulong ObjectId;
            public readonly int SourceIndex;
            public readonly NetworkVector3 Position;
            public readonly uint LayerMask;

            public Entry(ulong objectId, int sourceIndex, NetworkVector3 position, uint layerMask)
            {
                ObjectId = objectId;
                SourceIndex = sourceIndex;
                Position = position;
                LayerMask = layerMask;
            }
        }

        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Z;

            public CellKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(CellKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is CellKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = (hash * 397) ^ Y;
                    hash = (hash * 397) ^ Z;
                    return hash;
                }
            }
        }
    }
}
