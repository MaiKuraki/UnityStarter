using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Optimized Blackboard implementation using int keys (hashes) and separate storage for primitives to avoid boxing.
    /// Thread-safe when used within a single RuntimeBehaviorTree instance.
    /// </summary>
    public class RuntimeBlackboard
    {
        private readonly Dictionary<int, int> _intData = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _floatData = new Dictionary<int, float>();
        private readonly Dictionary<int, bool> _boolData = new Dictionary<int, bool>();
        private readonly Dictionary<int, object> _objectData = new Dictionary<int, object>();
        // Using object for vectors to keep core pure C# referencing System.Numerics later if needed, 
        // or we can add specific dictionary for Unity Vector if we decide to keep dependency for convenience.
        // For now, storing Vectors in _objectData involves boxing, but we can optimize later with specific structs if needed.
        // Or we can add:
        // private readonly Dictionary<int, System.Numerics.Vector3> _vector3Data ...

        public RuntimeBlackboard Parent { get; private set; }

        public RuntimeBlackboard(RuntimeBlackboard parent = null)
        {
            Parent = parent;
        }

        public void SetInt(int key, int value) => _intData[key] = value;
        public int GetInt(int key, int defaultValue = 0)
        {
            if (_intData.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetInt(key, defaultValue) : defaultValue;
        }

        public void SetFloat(int key, float value) => _floatData[key] = value;
        public float GetFloat(int key, float defaultValue = 0f)
        {
            if (_floatData.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetFloat(key, defaultValue) : defaultValue;
        }

        public void SetBool(int key, bool value) => _boolData[key] = value;
        public bool GetBool(int key, bool defaultValue = false)
        {
            if (_boolData.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetBool(key, defaultValue) : defaultValue;
        }

        public void SetObject(int key, object value) => _objectData[key] = value;
        public T GetObject<T>(int key)
        {
            if (_objectData.TryGetValue(key, out var val))
            {
                if (val is T tVal) return tVal;
            }
            return Parent != null ? Parent.GetObject<T>(key) : default;
        }
        
        public bool HasKey(int key)
        {
            return _intData.ContainsKey(key) || 
                   _floatData.ContainsKey(key) || 
                   _boolData.ContainsKey(key) || 
                   _objectData.ContainsKey(key) ||
                   (Parent != null && Parent.HasKey(key));
        }

        public void Clear()
        {
            _intData.Clear();
            _floatData.Clear();
            _boolData.Clear();
            _objectData.Clear();
        }
    }
}
