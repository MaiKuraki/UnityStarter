using System;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Scripting;

namespace Proxima
{
    public interface IPropertyOrValue
    {
        object Get(Type type);
    }

    public struct PropertyOrValue<T> : IPropertyOrValue
    {
        private string _pattern;

        [Preserve]
        public PropertyOrValue(string pattern)
        {
            _pattern = pattern;
        }

        public bool IsSet => !string.IsNullOrEmpty(_pattern);

        public T Get()
        {
            return (T)Get(typeof(T));
        }

        public object Get(Type type)
        {
            var properties = ProximaCommandHelpers.FindProperties(_pattern);
            if (properties.Count > 1)
            {
                throw new Exception("Multiple properties found that match pattern.");
            }

            if (properties.Count == 1)
            {
                var property = properties[0];
                if (property.CanRead && property.Type.IsAssignableFrom(type))
                {
                    return property.GetValue();
                }
            }

            return ProximaSerialization.Deserialize(type, _pattern);
        }

        public T GetOrDefault(T defaultValue = default)
        {
            if (!IsSet)
            {
                return defaultValue;
            }

            return Get();
        }
    }

    public static class ProximaCommandHelpers
    {
        public static List<GameObject> FindGameObjects(string name)
        {
            #if UNITY_2023_1_OR_NEWER
                var allGameObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            #elif UNITY_2020_1_OR_NEWER
                var allGameObjects = GameObject.FindObjectsOfType<GameObject>(true);
            #else
                #pragma warning disable 612, 618
                var allGameObjects = (GameObject[]) GameObject.FindObjectsOfTypeAll(typeof(GameObject));
                #pragma warning restore 612, 618
            #endif

            var objects = new List<GameObject>();

            if (name.StartsWith("[") && name.EndsWith("]") && int.TryParse(name.Substring(1, name.Length - 2), out var id))
            {
                foreach (var o in allGameObjects)
                {
                    if (o.GetInstanceID() == id)
                    {
                        objects.Add(o);
                        break;
                    }
                }
            }
            else
            {
                var regex = new Regex("^" + Regex.Escape(name).Replace("\\*", ".*?") + "$", RegexOptions.IgnoreCase);
                foreach (var o in allGameObjects)
                {
                    if (o.scene.isLoaded && regex.IsMatch(o.name))
                    {
                        objects.Add(o);
                    }
                }
            }

            objects.Sort((a, b) => a.name.ToLower().CompareTo(b.name.ToLower()));
            return objects;
        }

        internal static List<PropertyOrField> FindProperties(string pattern)
        {
            var result = new List<PropertyOrField>();
            var idx = pattern.IndexOf('.');
            if (idx == -1)
            {
                return result;
            }

            var objectName = pattern.Substring(0, idx);
            var rest = pattern.Substring(idx + 1);

            foreach (var unityStatic in UnityStatics)
            {
                if (unityStatic.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                {
                    AddStaticProperty(result, unityStatic, rest);
                    return result;
                }
            }

            var gameObjects = FindGameObjects(objectName);
            foreach (var o in gameObjects)
            {
                result.AddRange(FindGameObjectProperties(o, rest));
            }

            return result;
        }

        public static Type FindFirstComponentType(string className)
        {
            var fullType = Type.GetType(className, false, true);
            if (fullType != null)
            {
                return fullType.IsSubclassOf(typeof(UnityEngine.Component)) ? fullType : null;
            }

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in a.GetTypes())
                {
                    if (type.IsSubclassOf(typeof(UnityEngine.Component)) &&
                        type.Name.Equals(className, StringComparison.OrdinalIgnoreCase))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static bool ComponentMatchesPattern(Component component, string pattern)
        {
            var type = component.GetType();
            while (type != typeof(Component))
            {
                if (type.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        private static List<PropertyOrField> FindGameObjectProperties(GameObject obj, string pattern)
        {
            var descriptor = obj.name + " [" + obj.GetInstanceID() + "]";
            var result = new List<PropertyOrField>();
            if (TryAddObjectProperty(result, descriptor, obj, pattern))
            {
                return result;
            }

            var idx = pattern.IndexOf('.');
            if (idx >= 0)
            {
                var componentName = pattern.Substring(0, idx);
                var rest = pattern.Substring(idx + 1);
                foreach (var component in obj.GetComponents<Component>())
                {
                    if (ComponentMatchesPattern(component, componentName))
                    {
                        TryAddObjectProperty(result, descriptor, component, rest);
                    }
                }
            }
            else
            {
                foreach (var component in obj.GetComponents<Component>())
                {
                    if (ComponentMatchesPattern(component, pattern))
                    {
                        result.Add(new PropertyOrField(descriptor, component));
                    }
                }
            }

            return result;
        }

        private static bool TryAddObjectProperty(List<PropertyOrField> list, string descriptor, object obj, string pattern)
        {
            var result = new List<PropertyOrField>();
            var prop = obj.GetType().GetProperty(pattern, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var field = obj.GetType().GetField(pattern, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null || field != null)
            {
                list.Add(new PropertyOrField(descriptor, obj, prop, field));
                return true;
            }

            return false;
        }

        private static void AddStaticProperty(List<PropertyOrField> list, Type staticType, string pattern)
        {
            var result = new List<PropertyOrField>();
            var prop = staticType.GetProperty(pattern, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            var field = staticType.GetField(pattern, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (prop != null || field != null)
            {
                var name = prop != null ? prop.Name : field.Name;
                list.Add(new PropertyOrField(staticType.Name + "." + name, null, prop, field));
            }
        }

        private static Type[] UnityStatics = new Type[] {
            typeof(Application), typeof(Time), typeof(Screen), typeof(QualitySettings), typeof(Input),

            #if UNITY_PHYSICS
                typeof(Physics),
            #endif

            #if UNITY_AUDIO
                typeof(AudioSettings),
            #endif

            #if UNITY_PHYSICS_2D
                typeof(Physics2D)
            #endif
        };
    }

    internal class PropertyOrField
    {
        public string Descriptor { get; set; }
        public object Object { get; set; }
        public PropertyInfo Property { get; set; }
        public FieldInfo Field { get; set; }
        public object Value { get; set; }

        public PropertyOrField(string descriptor, object obj, PropertyInfo property, FieldInfo field)
        {
            Descriptor = descriptor;
            Object = obj;
            Property = property;
            Field = field;
        }

        public PropertyOrField(string descriptor, object value)
        {
            Descriptor = descriptor;
            Value = value;
        }

        public void SetValue(object value)
        {
            if (Property != null && Property.CanWrite)
            {
                Property.SetValue(Object, value);
            }
            else if (Field != null)
            {
                Field.SetValue(Object, value);
            }
        }

        public object GetValue()
        {
            if (Property != null && Property.CanRead)
            {
                return Property.GetValue(Object);
            }
            else if (Field != null)
            {
                return Field.GetValue(Object);
            }
            else
            {
                return Value;
            }
        }

        public Type Type
        {
            get
            {
                if (Property != null)
                {
                    return Property.PropertyType;
                }
                else if (Field != null)
                {
                    return Field.FieldType;
                }
                else
                {
                    return Value?.GetType();
                }
            }
        }

        public bool CanWrite
        {
            get
            {
                if (Property != null)
                {
                    return Property.CanWrite;
                }
                else
                {
                    return Field != null;
                }
            }
        }

        public bool CanRead
        {
            get
            {
                if (Property != null)
                {
                    return Property.CanRead;
                }
                else
                {
                    return Field != null || Value != null;
                }
            }
        }
    }
}