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
        private readonly Dictionary<string, Vector2> _vector2Data = new Dictionary<string, Vector2>(4, StringComparer.Ordinal);
        private readonly Dictionary<string, Quaternion> _quaternionData = new Dictionary<string, Quaternion>(4, StringComparer.Ordinal);
        private readonly Dictionary<string, int> _enumData = new Dictionary<string, int>(4, StringComparer.Ordinal);

        public BlackBoard() { }

        public BlackBoard(BlackBoard parent)
        {
            Parent = parent;
        }

        #region Generic Access
        public object Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (_data.TryGetValue(key, out var value)) return value;
            if (_intData.TryGetValue(key, out var intVal)) return intVal;
            if (_floatData.TryGetValue(key, out var floatVal)) return floatVal;
            if (_boolData.TryGetValue(key, out var boolVal)) return boolVal;
            if (_vector3Data.TryGetValue(key, out var vec3Val)) return vec3Val;
            if (_vector2Data.TryGetValue(key, out var vec2Val)) return vec2Val;
            if (_quaternionData.TryGetValue(key, out var quatVal)) return quatVal;

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
            else if (type == typeof(Vector2))
            {
                if (_vector2Data.TryGetValue(key, out var val)) return (T)(object)val;
            }
            else if (type == typeof(Quaternion))
            {
                if (_quaternionData.TryGetValue(key, out var val)) return (T)(object)val;
            }
            else if (type.IsEnum)
            {
                if (_enumData.TryGetValue(key, out var val)) return (T)Enum.ToObject(type, val);
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
            else if (value is Vector3 v3) _vector3Data[key] = v3;
            else if (value is Vector2 v2) _vector2Data[key] = v2;
            else if (value is Quaternion q) _quaternionData[key] = q;
            else if (value != null && value.GetType().IsEnum) _enumData[key] = Convert.ToInt32(value);
            else _data[key] = value;
        }
        #endregion

        #region Typed Access
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

        public Vector2 GetVector2(string key, Vector2 defaultValue = default)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (_vector2Data.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetVector2(key, defaultValue) : defaultValue;
        }

        public void SetVector2(string key, Vector2 value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _vector2Data[key] = value;
        }

        public Quaternion GetQuaternion(string key, Quaternion defaultValue = default)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (_quaternionData.TryGetValue(key, out var val)) return val;
            return Parent != null ? Parent.GetQuaternion(key, defaultValue) : defaultValue;
        }

        public void SetQuaternion(string key, Quaternion value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _quaternionData[key] = value;
        }

        public T GetEnum<T>(string key, T defaultValue = default) where T : Enum
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (_enumData.TryGetValue(key, out var val))
            {
                return (T)Enum.ToObject(typeof(T), val);
            }
            return Parent != null ? Parent.GetEnum(key, defaultValue) : defaultValue;
        }

        public void SetEnum<T>(string key, T value) where T : Enum
        {
            if (string.IsNullOrEmpty(key)) return;
            _enumData[key] = Convert.ToInt32(value);
        }
        #endregion

        #region Batch Operations
        /// <summary>
        /// Sets multiple values in a single operation to reduce dictionary lookup overhead.
        /// </summary>
        public void SetBatch(Dictionary<string, object> values)
        {
            if (values == null) return;
            foreach (var kvp in values)
            {
                Set(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Gets multiple values in a single operation to reduce dictionary lookup overhead.
        /// </summary>
        public Dictionary<string, object> GetBatch(string[] keys)
        {
            var result = new Dictionary<string, object>(keys.Length);
            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = Get(key);
                }
            }
            return result;
        }
        #endregion

        public Dictionary<string, object> GetAllData()
        {
            var result = new Dictionary<string, object>(
                _data.Count + _intData.Count + _floatData.Count + _boolData.Count +
                _vector3Data.Count + _vector2Data.Count + _quaternionData.Count + _enumData.Count);

            foreach (var kvp in _data) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _intData) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _floatData) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _boolData) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _vector3Data) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _vector2Data) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _quaternionData) result[kvp.Key] = kvp.Value;
            foreach (var kvp in _enumData) result[kvp.Key] = kvp.Value;

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

        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (_data.ContainsKey(key)) return true;
            if (_intData.ContainsKey(key)) return true;
            if (_floatData.ContainsKey(key)) return true;
            if (_boolData.ContainsKey(key)) return true;
            if (_vector3Data.ContainsKey(key)) return true;
            if (_vector2Data.ContainsKey(key)) return true;
            if (_quaternionData.ContainsKey(key)) return true;
            if (_enumData.ContainsKey(key)) return true;

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
            _vector2Data.Remove(key);
            _quaternionData.Remove(key);
            _enumData.Remove(key);
        }

        public void Clear()
        {
            _data.Clear();
            _intData.Clear();
            _floatData.Clear();
            _boolData.Clear();
            _vector3Data.Clear();
            _vector2Data.Clear();
            _quaternionData.Clear();
            _enumData.Clear();
        }
    }
}