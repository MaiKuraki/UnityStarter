using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes
{
    [Serializable]
    public class BlackBoard
    {
        [HideInInspector] public BlackBoard Parent;

        private readonly Dictionary<string, object> _data = new Dictionary<string, object>(16, StringComparer.Ordinal);

        public BlackBoard() { }

        public BlackBoard(BlackBoard parent)
        {
            Parent = parent;
        }

        public Dictionary<string, object> GetAllData()
        {
            var result = new Dictionary<string, object>(_data.Count);
            foreach (var kvp in _data)
            {
                result[kvp.Key] = kvp.Value;
            }

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

        public object Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (_data.TryGetValue(key, out var value))
            {
                return value;
            }

            return Parent?.Get(key);
        }

        public T Get<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) return default;

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

        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (_data.ContainsKey(key)) return true;
            return Parent != null && Parent.Contains(key);
        }

        public void Set(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _data[key] = value;
        }

        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _data.Remove(key);
        }

        public void Clear()
        {
            _data.Clear();
        }
    }
}