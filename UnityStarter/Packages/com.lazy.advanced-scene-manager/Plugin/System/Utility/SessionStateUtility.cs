using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace AdvancedSceneManager.Utility
{

    internal static class SessionStateUtility
    {

        static readonly Dictionary<string, object> cache = new();

        public static void Set<T>(T value, [CallerMemberName] string propertyName = "")
        {

            var key = $"ASM.{propertyName}";

            cache.Set(key, value);

#if UNITY_EDITOR
            if (typeof(T) == typeof(string))
                SessionState.SetString(key, (string)(object)value);
            else if (typeof(T) == typeof(float))
                SessionState.SetFloat(key, (float)(object)value);
            else if (typeof(T) == typeof(bool))
                SessionState.SetBool(key, (bool)(object)value);
            else if (typeof(T) == typeof(int))
                SessionState.SetInt(key, (int)(object)value);
            else if (typeof(T) == typeof(int[]))
                SessionState.SetIntArray(key, (int[])(object)value);
            else if (typeof(T) == typeof(Vector3))
                SessionState.SetVector3(key, (Vector3)(object)value);
            else
                SessionState.SetString(key, JsonUtility.ToJson(value));
#endif

        }

        public static T Get<T>(T defaultValue = default, [CallerMemberName] string propertyName = "")
        {

            var key = $"ASM.{propertyName}";

            if (cache.TryGetValue(key, out var value) && value is T t)
                return t;

#if UNITY_EDITOR
            if (typeof(T) == typeof(string))
                return (T)(object)SessionState.GetString(key, (string)(object)defaultValue);
            else if (typeof(T) == typeof(float))
                return (T)(object)SessionState.GetFloat(key, (float)(object)defaultValue);
            else if (typeof(T) == typeof(bool))
                return (T)(object)SessionState.GetBool(key, (bool)(object)defaultValue);
            else if (typeof(T) == typeof(int))
                return (T)(object)SessionState.GetInt(key, (int)(object)defaultValue);
            else if (typeof(T) == typeof(int[]))
                return (T)(object)SessionState.GetIntArray(key, (int[])(object)defaultValue);
            else if (typeof(T) == typeof(Vector3))
                return (T)(object)SessionState.GetVector3(key, (Vector3)(object)defaultValue);
            else
            {
                try
                {
                    return JsonUtility.FromJson<T>(SessionState.GetString(key, string.Empty));
                }
                catch
                {
                    return default;
                }
            }
#else
            return default;
#endif


        }

    }

}