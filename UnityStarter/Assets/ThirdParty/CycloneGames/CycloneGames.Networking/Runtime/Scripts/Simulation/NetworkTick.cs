using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Simulation
{
    /// <summary>
    /// Network tick counter. Value type for zero-alloc tick arithmetic.
    /// </summary>
    public readonly struct NetworkTick : IEquatable<NetworkTick>, IComparable<NetworkTick>
    {
        public readonly uint Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NetworkTick(uint value) => Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNewerThan(NetworkTick other)
        {
            // Handles uint wraparound using half-range comparison
            return (int)(Value - other.Value) > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TicksSince(NetworkTick other) => (int)(Value - other.Value);

        public static NetworkTick operator +(NetworkTick a, uint b) => new NetworkTick(a.Value + b);
        public static NetworkTick operator -(NetworkTick a, uint b) => new NetworkTick(a.Value - b);
        public static NetworkTick operator ++(NetworkTick a) => new NetworkTick(a.Value + 1);
        public static bool operator ==(NetworkTick a, NetworkTick b) => a.Value == b.Value;
        public static bool operator !=(NetworkTick a, NetworkTick b) => a.Value != b.Value;
        public static bool operator >(NetworkTick a, NetworkTick b) => a.IsNewerThan(b);
        public static bool operator <(NetworkTick a, NetworkTick b) => b.IsNewerThan(a);

        public bool Equals(NetworkTick other) => Value == other.Value;
        public int CompareTo(NetworkTick other) => (int)(Value - other.Value);
        public override bool Equals(object obj) => obj is NetworkTick t && Equals(t);
        public override int GetHashCode() => (int)Value;
        public override string ToString() => Value.ToString();

        public static readonly NetworkTick Zero = new NetworkTick(0);
    }

    public interface ITickable
    {
        void OnNetworkTick(NetworkTick tick, float tickDeltaTime);
    }

    public interface ITickSystem
    {
        NetworkTick CurrentTick { get; }
        int TickRate { get; }
        float TickInterval { get; }
        float ServerTime { get; }
        float InterpolationDelay { get; }

        event Action<NetworkTick> OnTick;
        event Action<NetworkTick> OnPreTick;
        event Action<NetworkTick> OnPostTick;

        void RegisterTickable(ITickable tickable);
        void UnregisterTickable(ITickable tickable);
    }
}
