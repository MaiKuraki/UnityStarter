using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.Networking.StateSync
{
    /// <summary>
    /// Interface for a network-synchronized variable.
    /// Changes are tracked via dirty flags and serialized only when modified.
    /// </summary>
    public interface INetworkVariable
    {
        bool IsDirty { get; }
        void ClearDirty();
        void WriteTo(Serialization.INetWriter writer);
        void ReadFrom(Serialization.INetReader reader);
    }

    /// <summary>
    /// Generic network-synchronized variable with change notification.
    /// Zero-allocation for unmanaged types via blittable serialization.
    /// 
    /// Usage:
    ///   var health = new NetworkVariable<int>(100);
    ///   health.Value = 80; // marks dirty, fires OnChanged
    /// </summary>
    public sealed class NetworkVariable<T> : INetworkVariable where T : unmanaged, IEquatable<T>
    {
        private T _value;
        private volatile bool _dirty;

        public event Action<T, T> OnChanged; // oldValue, newValue

        public T Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Thread.MemoryBarrier();
                return _value;
            }
            set
            {
                if (_value.Equals(value)) return;
                T old = _value;
                _value = value;
                Thread.MemoryBarrier();
                _dirty = true;
                OnChanged?.Invoke(old, value);
            }
        }

        public bool IsDirty => _dirty;

        public NetworkVariable() { }
        public NetworkVariable(T initialValue) { _value = initialValue; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearDirty() => _dirty = false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTo(Serialization.INetWriter writer) => writer.WriteBlittable(_value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadFrom(Serialization.INetReader reader)
        {
            T newValue = reader.ReadBlittable<T>();
            if (!_value.Equals(newValue))
            {
                T old = _value;
                _value = newValue;
                OnChanged?.Invoke(old, newValue);
            }
        }

        // Force set without dirty tracking (initial sync)
        public void SetSilent(T value) => _value = value;
        public void SetDirty() => _dirty = true;

        public override string ToString() => _value.ToString();

        public static implicit operator T(NetworkVariable<T> nv) => nv._value;
    }

    /// <summary>
    /// NetworkVariable for managed types using INetSerializer.
    /// Supports any serializable struct type but may produce allocations.
    /// </summary>
    public sealed class NetworkVariableManaged<T> : INetworkVariable where T : struct
    {
        private T _value;
        private bool _dirty;
        private readonly Serialization.INetSerializer _serializer;

        public event Action<T, T> OnChanged;

        public T Value
        {
            get => _value;
            set
            {
                T old = _value;
                _value = value;
                _dirty = true;
                OnChanged?.Invoke(old, value);
            }
        }

        public bool IsDirty => _dirty;

        public NetworkVariableManaged(Serialization.INetSerializer serializer)
        {
            _serializer = serializer;
        }

        public NetworkVariableManaged(Serialization.INetSerializer serializer, T initialValue) : this(serializer)
        {
            _value = initialValue;
        }

        public void ClearDirty() => _dirty = false;

        public void WriteTo(Serialization.INetWriter writer)
        {
            _serializer.Serialize(_value, writer);
        }

        public void ReadFrom(Serialization.INetReader reader)
        {
            T old = _value;
            _value = _serializer.Deserialize<T>(reader);
            OnChanged?.Invoke(old, _value);
        }

        public void SetSilent(T value) => _value = value;
    }
}
