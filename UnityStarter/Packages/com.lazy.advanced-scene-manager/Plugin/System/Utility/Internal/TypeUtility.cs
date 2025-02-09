using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace AdvancedSceneManager.Utility
{

    static class TypeUtility
    {

        public static IEnumerable<FieldInfo> _GetFields(this Type type)
        {

            foreach (var field in type.GetFields(BindingFlags.GetField | BindingFlags.SetField | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                yield return field;

            if (type.BaseType != null)
                foreach (var field in _GetFields(type.BaseType))
                    yield return field;

        }

        public static FieldInfo FindField(this Type type, string name)
        {
            var e = _GetFields(type).GetEnumerator();
            while (e.MoveNext())
                if (e.Current.Name == name)
                    return e.Current;
            return null;
        }

#if UNITY_EDITOR
        /// <summary>Finds all assets of this type in the project, and return their paths.</summary>
        /// <remarks>Only available in the editor.</remarks>
        public static IEnumerable<string> FindAssetPaths(this Type type) =>
            AssetDatabase.FindAssets("t:" + type.FullName).Select(AssetDatabase.GUIDToAssetPath);
#endif

        public static IEnumerable<(MethodInfo method, TAttribute attribute)> FindMethodsDecoratedWithAttribute<TAttribute>(BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) where TAttribute : Attribute
        {

            if (!typeof(Attribute).IsAssignableFrom(typeof(TAttribute)))
                throw new ArgumentException($"{typeof(TAttribute).Name} is not an attribute type.");

            return AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch (Exception)
                    {
                        return Type.EmptyTypes;
                    }
                })
                .SelectMany(type => type
                    .GetMethods(bindingFlags)
                    .SelectMany(m => m.GetCustomAttributes<TAttribute>().Select(attribute => (m, attribute)))
                )
                .Where(m => m.attribute is not null);

        }

        public static IEnumerable<(Type type, TAttribute attribute)> FindClassesDecoratedWithAttribute<TAttribute>() where TAttribute : Attribute
        {

            if (!typeof(Attribute).IsAssignableFrom(typeof(TAttribute)))
                throw new ArgumentException($"{typeof(TAttribute).Name} is not an attribute type.");

            return AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch (Exception)
                    {
                        return Type.EmptyTypes;
                    }
                })
                .SelectMany(t => t.GetCustomAttributes<TAttribute>().Select(attribute => (t, attribute)))
                .Where(t => t.attribute is not null);

        }

        public static IEnumerable<Type> FindSubclasses<T>()
        {
            var t = typeof(T);
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch (Exception)
                    {
                        return Type.EmptyTypes;
                    }
                })
                .Where(t.IsAssignableFrom)
                .Where(t1 => t1 != t);
        }

        public static IEnumerable<T> FindSubclassesAndInstantiate<T>()
        {
            return FindSubclasses<T>().Where(t => t.GetConstructor(Type.EmptyTypes) is not null).Select(t => (T)Activator.CreateInstance(t));
        }

    }

}