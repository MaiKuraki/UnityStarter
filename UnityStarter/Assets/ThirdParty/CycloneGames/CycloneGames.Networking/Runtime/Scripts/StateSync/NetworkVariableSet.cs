using System;
using System.Collections.Generic;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.StateSync
{
    /// <summary>
    /// Manages a collection of INetworkVariable instances for a single entity.
    /// Handles dirty tracking, serialization of only changed variables, and full state sync.
    /// </summary>
    public sealed class NetworkVariableSet
    {
        private readonly List<INetworkVariable> _variables = new List<INetworkVariable>(8);
        private DirtyFlags _dirtyFlags;

        public int Count => _variables.Count;

        /// <summary>
        /// Register a variable and return its index (0-63).
        /// </summary>
        public int Register(INetworkVariable variable)
        {
            int index = _variables.Count;
            if (index >= 64)
                throw new InvalidOperationException("Maximum 64 NetworkVariables per entity");
            _variables.Add(variable);
            return index;
        }

        /// <summary>
        /// Mark a variable as dirty by its index.
        /// </summary>
        public void MarkDirty(int index) => _dirtyFlags.SetDirty(index);

        public bool IsAnyDirty() => _dirtyFlags.IsAnyDirty();

        /// <summary>
        /// Write only dirty variables to the writer. Format: [dirtyMask:long][var0_data][var3_data]...
        /// </summary>
        public void WriteDirty(INetWriter writer)
        {
            long mask = 0;
            for (int i = 0; i < _variables.Count; i++)
            {
                if (_variables[i].IsDirty)
                    mask |= 1L << i;
            }

            writer.WriteBlittable(mask);

            for (int i = 0; i < _variables.Count; i++)
            {
                if ((mask & (1L << i)) != 0)
                {
                    _variables[i].WriteTo(writer);
                    _variables[i].ClearDirty();
                }
            }

            _dirtyFlags.Clear();
        }

        /// <summary>
        /// Read dirty variables from the reader.
        /// </summary>
        public void ReadDirty(INetReader reader)
        {
            long mask = reader.ReadBlittable<long>();

            for (int i = 0; i < _variables.Count; i++)
            {
                if ((mask & (1L << i)) != 0)
                    _variables[i].ReadFrom(reader);
            }
        }

        /// <summary>
        /// Write ALL variables (full state sync for initial spawn / late join).
        /// </summary>
        public void WriteFull(INetWriter writer)
        {
            for (int i = 0; i < _variables.Count; i++)
                _variables[i].WriteTo(writer);
        }

        /// <summary>
        /// Read ALL variables (full state sync).
        /// </summary>
        public void ReadFull(INetReader reader)
        {
            for (int i = 0; i < _variables.Count; i++)
                _variables[i].ReadFrom(reader);
        }

        public void ClearAllDirty()
        {
            _dirtyFlags.Clear();
            for (int i = 0; i < _variables.Count; i++)
                _variables[i].ClearDirty();
        }
    }
}
