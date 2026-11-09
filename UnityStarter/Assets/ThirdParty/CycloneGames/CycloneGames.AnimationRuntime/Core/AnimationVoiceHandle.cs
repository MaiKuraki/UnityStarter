using System;

namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>
    /// Lightweight value-type handle for one active clip voice. Carries the graph instance id, the slot and a
    /// generation counter, so a handle taken before a slot was recycled can never address the new occupant.
    /// Size is fixed at 16 bytes on 64-bit targets; it is part of the public contract.
    /// </summary>
    public readonly struct AnimationVoiceHandle : IEquatable<AnimationVoiceHandle>
    {
        /// <summary>Default value that never addresses a voice.</summary>
        public static readonly AnimationVoiceHandle Invalid = default;

        private readonly int _graphId;
        private readonly byte _layer;
        private readonly byte _slot;
        private readonly ushort _generation;

        public AnimationVoiceHandle(int graphId, int layer, int slot, int generation)
        {
            _graphId = graphId;
            _layer = layer < 0 ? (byte)0 : (layer > byte.MaxValue ? byte.MaxValue : (byte)layer);
            _slot = slot < 0 ? (byte)0 : (slot > byte.MaxValue ? byte.MaxValue : (byte)slot);
            _generation = generation < 0 ? (ushort)0 : (generation > ushort.MaxValue ? ushort.MaxValue : (ushort)generation);
        }

        /// <summary>Graph instance that produced this handle. Bumped on rebind and dispose.</summary>
        public int GraphId => _graphId;

        /// <summary>Layer index the voice lives on.</summary>
        public int Layer => _layer;

        /// <summary>Slot index inside the layer.</summary>
        public int Slot => _slot;

        /// <summary>Generation of the slot when the handle was taken.</summary>
        public int Generation => _generation;

        /// <summary>Whether the handle carries a graph id and a generation. Does not check liveness.</summary>
        public bool IsValid => _graphId != 0 && _generation != 0;

        public bool Equals(AnimationVoiceHandle other)
        {
            return _graphId == other._graphId
                && _layer == other._layer
                && _slot == other._slot
                && _generation == other._generation;
        }

        public override bool Equals(object obj)
        {
            return obj is AnimationVoiceHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _graphId;
                hash = (hash * 397) ^ _layer;
                hash = (hash * 397) ^ _slot;
                hash = (hash * 397) ^ _generation;
                return hash;
            }
        }

        public static bool operator ==(AnimationVoiceHandle left, AnimationVoiceHandle right) => left.Equals(right);

        public static bool operator !=(AnimationVoiceHandle left, AnimationVoiceHandle right) => !left.Equals(right);

        public override string ToString()
        {
            return "Voice(graph " + _graphId + ", layer " + _layer + ", slot " + _slot + ", gen " + _generation + ")";
        }
    }
}
