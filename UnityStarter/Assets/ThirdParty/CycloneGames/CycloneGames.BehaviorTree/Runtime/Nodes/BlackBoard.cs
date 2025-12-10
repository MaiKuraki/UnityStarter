using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes
{
    [Serializable]
    public class BlackBoard : IBlackBoard
    {
        [HideInInspector] public BlackBoard Parent;

        private readonly Dictionary<string, object> _data = new Dictionary<string, object>(16, StringComparer.Ordinal);
        private readonly Dictionary<string, int> _intData = new Dictionary<string, int>(8, StringComparer.Ordinal);
        private readonly Dictionary<string, float> _floatData = new Dictionary<string, float>(8, StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _boolData = new Dictionary<string, bool>(8, StringComparer.Ordinal);
        private readonly Dictionary<string, Vector3> _vector3Data = new Dictionary<string, Vector3>(8, StringComparer.Ordinal);

        public BlackBoard() { }

        public BlackBoard(BlackBoard parent)
        {
            Parent = parent;
        }

        public Dictionary<string, object> GetAllData()
        {
            var result = new Dictionary<string, object>(_data.Count + _intData.Count + _floatData.Count + _boolData.Count + _vector3Data.Count);
            foreach (var kvp in _data) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _intData) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _floatData) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _boolData) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _vector3Data) result[kvp.Key] = kvp.Value;

            if (Parent != null)
            {
                var parentData = Parent.GetAllData();
                foreach (var kvp in parentData)
                {
                    if (!result.ContainsKey(kvp.Key))
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                }
            }
            return result;
        }

        #region Generic Access
        public object Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (_data.TryGetValue(key, out var value)) return value;
            if (_intData.TryGetValue(key, out var intVal)) return intVal;
            if (_floatData.TryGetValue(key, out var floatVal)) return floatVal;
            if (_boolData.TryGetValue(key, out var boolVal)) return boolVal;
            if (_vector3Data.TryGetValue(key, out var vecVal)) return vecVal;

            return Parent?.Get(key);
        }

        public T Get<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) return default;

            Type type = typeof(T);
            if (type == typeof(int))
            {
                if (_intData.TryGetValue(key, out var val)) return (T)(object)val;
            }
            else if (type == typeof(float))
            {
                if (_floatData.TryGetValue(key, out var val)) return (T)(object)val;
            }
            else if (type == typeof(bool))
            {
                if (_boolData.TryGetValue(key, out var val)) return (T)(object)val;
            }
            else if (type == typeof(Vector3))
            {
                if (_vector3Data.TryGetValue(key, out var val)) return (T)(object)val;
            }

            if (_data.TryGetValue(key, out var value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }
                return default;
            }

            return Parent != null ? Parent.Get<T>(key) : default;
        }

        public void Set(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (value is int i) _intData[key] = i;
            else if (value is float f) _floatData[key] = f;
            else if (value is bool b) _boolData[key] = b;
            else if (value is Vector3 v) _vector3Data[key] = v;
            else _data[key] = value;
        }
        #endregion

        #region Typed Access (0GC)
        public int GetInt(string key, int defaultValue = 0)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (_intData.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetInt(key, defaultValue) : defaultValue;
        }

        public void SetInt(string key, int value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _intData[key] = value;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (_floatData.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetFloat(key, defaultValue) : defaultValue;
        }

        public void SetFloat(string key, float value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _floatData[key] = value;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (_boolData.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetBool(key, defaultValue) : defaultValue;
        }

        public void SetBool(string key, bool value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _boolData[key] = value;
        }

        public Vector3 GetVector3(string key, Vector3 defaultValue = default)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (_vector3Data.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetVector3(key, defaultValue) : defaultValue;
        }

        public void SetVector3(string key, Vector3 value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _vector3Data[key] = value;
        }
        #endregion

        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (_data.ContainsKey(key)) return true;
            if (_intData.ContainsKey(key)) return true;
            if (_floatData.ContainsKey(key)) return true;
            if (_boolData.ContainsKey(key)) return true;
            if (_vector3Data.ContainsKey(key)) return true;

            return Parent != null && Parent.Contains(key);
        }

        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _data.Remove(key);
            _intData.Remove(key);
            _floatData.Remove(key);
            _boolData.Remove(key);
            _vector3Data.Remove(key);
        }

        public void Clear()
        {
            _data.Clear();
            _intData.Clear();
            _floatData.Clear();
            _boolData.Clear();
            _vector3Data.Clear();
        }
    }
}