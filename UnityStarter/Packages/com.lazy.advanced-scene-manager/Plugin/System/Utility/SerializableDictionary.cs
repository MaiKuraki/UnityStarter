using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace AdvancedSceneManager.Utility
{

    /// <summary>A serializable dictionary of string and bool.</summary>
    [Serializable]
    public class SerializableStringBoolDict : SerializableDictionary<string, bool>
    { }

    /// <summary>A serializable dictionary.</summary>
    /// <remarks>Older unity versions might need a wrapper class, since they won't support serializing generic types. Don't forget <see cref="SerializableAttribute"/> on wrapper!</remarks>
    [Serializable]
    public class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver
    {

        [FormerlySerializedAs("m_throwOnDeserializeWhenKeyValueMismatch")]
        [SerializeField] private bool m_throw = true;

        public bool throwOnDeserializeWhenKeyValueMismatch
        {
            get => m_throw;
            set => m_throw = value;
        }

        [SerializeField]
        protected List<TKey> m_keys = new List<TKey>();

        [SerializeField]
        protected List<TValue> m_values = new List<TValue>();

        public new KeyCollection Keys => base.Keys;
        public new ValueCollection Values => base.Values;

        public virtual void OnBeforeSerialize()
        {
            m_keys = Keys.ToList();
            m_values = Values.ToList();
        }

        public virtual void OnAfterDeserialize()
        {

            Clear();

            if (m_keys.Count != m_values.Count)
                if (throwOnDeserializeWhenKeyValueMismatch)
                    throw new Exception(string.Format($"SerializableDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}>:\nThere are {m_keys.Count} keys and {m_values.Count} values after deserialization. Make sure that both key and value types are serializable."));
                else
                    return;

            for (int i = 0; i < m_keys.Count; i++)
                Add(m_keys[i], m_values[i]);

        }

    }

}
