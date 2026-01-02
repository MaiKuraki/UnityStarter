using System.Collections.Generic;
using UnityEngine;

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

        public RuntimeBlackboard Parent { get; private set; }

        public RuntimeBlackboard(RuntimeBlackboard parent = null)
        {
            Parent = parent;
        }

        #region Int-Key Methods (0GC)
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

        public void Remove(int key)
        {
            _intData.Remove(key);
            _floatData.Remove(key);
            _boolData.Remove(key);
            _objectData.Remove(key);
        }
        #endregion

        #region String-Key Convenience Methods
        public void SetInt(string key, int value) => SetInt(Animator.StringToHash(key), value);
        public int GetInt(string key, int defaultValue = 0) => GetInt(Animator.StringToHash(key), defaultValue);

        public void SetFloat(string key, float value) => SetFloat(Animator.StringToHash(key), value);
        public float GetFloat(string key, float defaultValue = 0f) => GetFloat(Animator.StringToHash(key), defaultValue);

        public void SetBool(string key, bool value) => SetBool(Animator.StringToHash(key), value);
        public bool GetBool(string key, bool defaultValue = false) => GetBool(Animator.StringToHash(key), defaultValue);

        public void SetObject(string key, object value) => SetObject(Animator.StringToHash(key), value);
        public T GetObject<T>(string key) => GetObject<T>(Animator.StringToHash(key));

        public bool HasKey(string key) => HasKey(Animator.StringToHash(key));
        public void Remove(string key) => Remove(Animator.StringToHash(key));
        #endregion

        public void Clear()
        {
            _intData.Clear();
            _floatData.Clear();
            _boolData.Clear();
            _objectData.Clear();
        }
    }
}
