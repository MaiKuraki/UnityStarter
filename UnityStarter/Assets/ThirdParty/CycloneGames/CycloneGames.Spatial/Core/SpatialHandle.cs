using System;

namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Opaque reference to one element stored in a spatial index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Valid while the element occupies its slot. The generation advances on reuse, so a removed handle can never
    /// match a later occupant; stale handles are reported with <c>false</c>, not exceptions.
    /// </para>
    /// <para>64-bit generation, skipping zero so <see cref="IsValid"/> stays meaningful.</para>
    /// </remarks>
    public readonly struct SpatialHandle : IEquatable<SpatialHandle>
    {
        /// <summary>Handle with <see cref="Slot"/> -1 and <see cref="Generation"/> 0.</summary>
        public static readonly SpatialHandle Invalid = new SpatialHandle(-1, 0UL);

        public SpatialHandle(int slot, ulong generation)
        {
            Slot = slot;
            Generation = generation;
        }

        /// <summary>Slot index inside the owning index. Not a stable identity across index instances.</summary>
        public int Slot { get; }

        /// <summary>Reuse counter of the slot. Zero means "never issued".</summary>
        public ulong Generation { get; }

        /// <summary>True when the handle was issued by an index, false for <see cref="Invalid"/>.</summary>
        public bool IsValid
        {
            get { return Generation != 0UL; }
        }

        public bool Equals(SpatialHandle other)
        {
            return Slot == other.Slot && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is SpatialHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            ulong value = ((ulong)(uint)Slot << 32) ^ Generation;
            return unchecked((int)(value ^ (value >> 32)));
        }

        public static bool operator ==(SpatialHandle left, SpatialHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SpatialHandle left, SpatialHandle right)
        {
            return !left.Equals(right);
        }

        /// <summary>Returns "slot:generation". Diagnostic use only; allocates.</summary>
        public override string ToString()
        {
            return Slot + ":" + Generation;
        }
    }
}
