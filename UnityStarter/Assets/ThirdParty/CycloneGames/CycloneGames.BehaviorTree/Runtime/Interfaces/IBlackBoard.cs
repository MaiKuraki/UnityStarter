using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Interfaces
{
    public interface IBlackBoard
    {
        // Generic object access (Legacy/Reference types)
        object Get(string key);
        T Get<T>(string key);
        void Set(string key, object value);
        
        // Typed access for 0GC value types
        int GetInt(string key, int defaultValue = 0);
        void SetInt(string key, int value);
        
        float GetFloat(string key, float defaultValue = 0f);
        void SetFloat(string key, float value);
        
        bool GetBool(string key, bool defaultValue = false);
        void SetBool(string key, bool value);
        
        Vector3 GetVector3(string key, Vector3 defaultValue = default);
        void SetVector3(string key, Vector3 value);
        
        bool Contains(string key);
        void Remove(string key);
        void Clear();
    }
}
