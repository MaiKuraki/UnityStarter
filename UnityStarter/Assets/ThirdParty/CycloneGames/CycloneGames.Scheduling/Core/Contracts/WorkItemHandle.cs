namespace CycloneGames.Scheduling.Core
{
    /// <summary>
    /// Identifies one accepted registration. SchedulerId makes a handle fail on a foreign
    /// scheduler instead of cancelling an unrelated registration.
    /// </summary>
    public readonly struct WorkItemHandle : System.IEquatable<WorkItemHandle>
    {
        public int Slot { get; }

        public uint Generation { get; }

        public uint SchedulerId { get; }

        /// <summary>True for the value returned when a registration was rejected.</summary>
        public bool IsDefault => Generation == 0;

        /// <summary>True when this handle was issued by the given scheduler identity.</summary>
        public bool BelongsTo(uint schedulerId)
        {
            return Generation != 0 && SchedulerId == schedulerId;
        }

        internal WorkItemHandle(int slot, uint generation, uint schedulerId)
        {
            Slot = slot;
            Generation = generation;
            SchedulerId = schedulerId;
        }

        public bool Equals(WorkItemHandle other)
        {
            return Slot == other.Slot
                && Generation == other.Generation
                && SchedulerId == other.SchedulerId;
        }

        public override bool Equals(object obj)
        {
            return obj is WorkItemHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Slot * 397;
                hash ^= (int)Generation;
                hash = hash * 397;
                hash ^= (int)SchedulerId;
                return hash;
            }
        }

        public static bool operator ==(WorkItemHandle left, WorkItemHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(WorkItemHandle left, WorkItemHandle right)
        {
            return !left.Equals(right);
        }
    }
}
