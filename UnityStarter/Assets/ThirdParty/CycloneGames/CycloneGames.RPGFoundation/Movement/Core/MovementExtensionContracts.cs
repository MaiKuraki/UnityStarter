using System;

namespace CycloneGames.RPGFoundation.Movement.Core
{
    public readonly struct MovementSemanticId : IEquatable<MovementSemanticId>
    {
        public readonly uint Value;

        public MovementSemanticId(uint value) { Value = value; }

        // FNV-1a over the name so peers, saved assets and CLI replays agree
        // without a central registry. Values below 1024 are reserved.
        public static MovementSemanticId FromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return new MovementSemanticId(0u);
            uint hash = 2166136261u;
            for (int i = 0; i < name.Length; i++)
            {
                hash ^= name[i];
                hash *= 16777619u;
            }
            if (hash < 1024u) hash |= 0x400u;
            return new MovementSemanticId(hash);
        }

        public bool IsValid { get { return Value != 0u; } }
        public bool IsBuiltIn { get { return Value != 0u && Value < 1024u; } }

        public bool Equals(MovementSemanticId other) { return Value == other.Value; }
        public override bool Equals(object obj) { return obj is MovementSemanticId other && Equals(other); }
        public override int GetHashCode() { return (int)Value; }
    }

    public sealed class MovementExtensionRegistry
    {
        private const int CapacityValue = 64;
        private readonly uint[] _ids = new uint[CapacityValue];
        private readonly string[] _names = new string[CapacityValue];
        private int _count;

        public static int Capacity { get { return CapacityValue; } }
        public int Count { get { return _count; } }

        public bool TryRegister(string name, out MovementSemanticId id)
        {
            id = MovementSemanticId.FromName(name);
            if (!id.IsValid) return false;
            for (int i = 0; i < _count; i++)
            {
                if (_ids[i] != id.Value) continue;
                return string.Equals(_names[i], name, StringComparison.Ordinal);
            }
            if (_count >= CapacityValue) return false;
            _ids[_count] = id.Value;
            _names[_count] = name;
            _count++;
            return true;
        }

        public bool TryResolve(MovementSemanticId id, out string name)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_ids[i] != id.Value) continue;
                name = _names[i];
                return true;
            }
            name = null;
            return false;
        }

        public int Write(Span<MovementSemanticId> destination)
        {
            int n = _count < destination.Length ? _count : destination.Length;
            for (int i = 0; i < n; i++) destination[i] = new MovementSemanticId(_ids[i]);
            return n;
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++) _names[i] = null;
            _count = 0;
        }
    }

    public interface IMovementPresentationBudget
    {
        bool ShouldPublish(in MovementAnimationState state, float distanceToViewer);
    }
}
