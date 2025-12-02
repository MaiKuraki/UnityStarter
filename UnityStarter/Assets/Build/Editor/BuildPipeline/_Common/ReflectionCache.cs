using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Caches reflection results to improve performance.
    /// Reduces repeated reflection calls for types, methods, properties, and fields.
    /// </summary>
    public static class ReflectionCache
    {
        private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
        private static readonly Dictionary<string, MethodInfo> _methodCache = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, PropertyInfo> _propertyCache = new Dictionary<string, PropertyInfo>();
        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>();

        static ReflectionCache()
        {
            // Clear cache when assemblies are reloaded
            AssemblyReloadEvents.afterAssemblyReload += ClearCache;
        }

        /// <summary>
        /// Gets a type by name, using cache if available.
        /// </summary>
        public static Type GetType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            if (_typeCache.TryGetValue(typeName, out Type cachedType))
            {
                return cachedType;
            }

            Type type = BuildUtils.GetTypeInAllAssemblies(typeName);
            _typeCache[typeName] = type;
            return type;
        }

        /// <summary>
        /// Gets a method from a type, using cache if available.
        /// </summary>
        public static MethodInfo GetMethod(Type type, string methodName, BindingFlags bindingFlags)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
                return null;

            string cacheKey = $"{type.FullName}.{methodName}.{bindingFlags}";
            if (_methodCache.TryGetValue(cacheKey, out MethodInfo cachedMethod))
            {
                return cachedMethod;
            }

            try
            {
                MethodInfo method = type.GetMethod(methodName, bindingFlags);
                _methodCache[cacheKey] = method;
                return method;
            }
            catch (AmbiguousMatchException)
            {
                // If multiple overloads exist, return null to let caller use GetMethod with parameter types
                return null;
            }
        }

        /// <summary>
        /// Gets a method from a type with specific parameter types, using cache if available.
        /// Use this when there are multiple overloads of the same method name.
        /// </summary>
        public static MethodInfo GetMethod(Type type, string methodName, BindingFlags bindingFlags, Type[] parameterTypes)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
                return null;

            string paramTypesStr = parameterTypes != null ? string.Join(",", Array.ConvertAll(parameterTypes, t => t?.FullName ?? "null")) : "null";
            string cacheKey = $"{type.FullName}.{methodName}.{bindingFlags}.{paramTypesStr}";
            if (_methodCache.TryGetValue(cacheKey, out MethodInfo cachedMethod))
            {
                return cachedMethod;
            }

            MethodInfo method = type.GetMethod(methodName, bindingFlags, null, parameterTypes ?? Type.EmptyTypes, null);
            _methodCache[cacheKey] = method;
            return method;
        }

        /// <summary>
        /// Gets a property from a type, using cache if available.
        /// </summary>
        public static PropertyInfo GetProperty(Type type, string propertyName, BindingFlags bindingFlags)
        {
            if (type == null || string.IsNullOrEmpty(propertyName))
                return null;

            string cacheKey = $"{type.FullName}.{propertyName}.{bindingFlags}";
            if (_propertyCache.TryGetValue(cacheKey, out PropertyInfo cachedProperty))
            {
                return cachedProperty;
            }

            PropertyInfo property = type.GetProperty(propertyName, bindingFlags);
            _propertyCache[cacheKey] = property;
            return property;
        }

        /// <summary>
        /// Gets a field from a type, using cache if available.
        /// </summary>
        public static FieldInfo GetField(Type type, string fieldName, BindingFlags bindingFlags)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
                return null;

            string cacheKey = $"{type.FullName}.{fieldName}.{bindingFlags}";
            if (_fieldCache.TryGetValue(cacheKey, out FieldInfo cachedField))
            {
                return cachedField;
            }

            FieldInfo field = type.GetField(fieldName, bindingFlags);
            _fieldCache[cacheKey] = field;
            return field;
        }

        /// <summary>
        /// Clears all reflection caches. Call this when assemblies are reloaded.
        /// </summary>
        public static void ClearCache()
        {
            _typeCache.Clear();
            _methodCache.Clear();
            _propertyCache.Clear();
            _fieldCache.Clear();
        }
    }
}