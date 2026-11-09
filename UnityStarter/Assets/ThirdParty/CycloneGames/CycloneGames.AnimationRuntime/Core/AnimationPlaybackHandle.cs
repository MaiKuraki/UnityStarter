using System;

namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>
    /// Lightweight value-type handle for one active clip playback. Carries the graph instance id, the slot and a
    /// 32-bit generation counter, so a handle taken before a slot was recycled can never address the new occupant.
    /// Size is 12 bytes on both 32-bit and 64-bit targets (4 + 1 + 1 + 4, aligned to 4); it is part of the public
    /// contract and guarded by a test.
    /// </summary>
    public readonly struct AnimationPlaybackHandle : IEquatable<AnimationPlaybackHandle>
    {
        /// <summary>Default value that never addresses a playback.</summary>
        public static readonly AnimationPlaybackHandle Invalid = default;

        private readonly int _graphId;
        private readonly byte _layer;
        private readonly byte _slot;
        private readonly uint _generation;

        public AnimationPlaybackHandle(int graphId, int layer, int slot, uint generation)
        {
            _graphId = graphId;
            _layer = layer < 0 ? (byte)0 : (layer > byte.MaxValue ? byte.MaxValue : (byte)layer);
            _slot = slot < 0 ? (byte)0 : (slot > byte.MaxValue ? byte.MaxValue : (byte)slot);
            _generation = generation;
        }

        /// <summary>Graph instance that produced this handle. Bumped on rebind, dispose or generation wrap.</summary>
        public int GraphId => _graphId;

        /// <summary>Layer index the playback lives on.</summary>
        public int Layer => _layer;

        /// <summary>Slot index inside the layer.</summary>
        public int Slot => _slot;

        /// <summary>32-bit slot generation at the time the handle was taken. Zero means "never issued".</summary>
        public uint Generation => _generation;

        /// <summary>Whether the handle carries a graph id and a generation. Does not check liveness.</summary>
        public bool IsValid => _graphId != 0 && _generation != 0u;

        public bool Equals(AnimationPlaybackHandle other)
        {
            return _graphId == other._graphId
                && _layer == other._layer
                && _slot == other._slot
                && _generation == other._generation;
        }

        public override bool Equals(object obj)
        {
            return obj is AnimationPlaybackHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _graphId;
                hash = (hash * 397) ^ _layer;
                hash = (hash * 397) ^ _slot;
                hash = (hash * 397) ^ (int)_generation;
                return hash;
            }
        }

        public static bool operator ==(AnimationPlaybackHandle left, AnimationPlaybackHandle right) => left.Equals(right);

        public static bool operator !=(AnimationPlaybackHandle left, AnimationPlaybackHandle right) => !left.Equals(right);

        public override string ToString()
        {
            return "Playback(graph " + _graphId + ", layer " + _layer + ", slot " + _slot + ", gen " + _generation + ")";
        }
    }
}
