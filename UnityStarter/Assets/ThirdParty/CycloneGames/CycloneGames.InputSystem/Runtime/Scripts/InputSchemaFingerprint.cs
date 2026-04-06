using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CycloneGames.Utility.Runtime;
using VYaml.Annotations;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Computes a deterministic fingerprint of the InputConfiguration schema using reflection.
    /// The fingerprint changes automatically when any [YamlMember] property, type, or enum value changes.
    /// Uses XxHash64 from CycloneGames.Utility.Runtime for cross-platform determinism.
    /// </summary>
    public static class InputSchemaFingerprint
    {
        private static string _cached;

        /// <summary>
        /// The current schema fingerprint. Computed once via reflection, then cached.
        /// </summary>
        public static string Current => _cached ??= Compute(typeof(InputConfiguration));

        private static string Compute(Type rootType)
        {
            var sb = new StringBuilder(256);
            var visited = new HashSet<Type>();
            AppendType(sb, rootType, visited);

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hasher = XxHash64.Create();
            hasher.Append(bytes);
            return hasher.GetDigest().ToString("x16");
        }

        private const int MaxDepth = 8;

        private static void AppendType(StringBuilder sb, Type type, HashSet<Type> visited, int depth = 0)
        {
            if (depth > MaxDepth || !visited.Add(type)) return;

            if (type.IsEnum)
            {
                sb.Append("enum:").Append(type.Name).Append('{');
                var names = Enum.GetNames(type);
                Array.Sort(names, StringComparer.Ordinal);
                for (int i = 0; i < names.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(names[i]);
                }
                sb.Append('}');
                return;
            }

            sb.Append("type:").Append(type.Name).Append('{');

            // Collect all [YamlMember] properties, sorted by YAML key for determinism
            var members = new List<(string yamlKey, Type propType)>();
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<YamlMemberAttribute>();
                if (attr == null) continue;
                string yamlKey = attr.Name ?? prop.Name;
                members.Add((yamlKey, prop.PropertyType));
            }
            members.Sort((a, b) => string.Compare(a.yamlKey, b.yamlKey, StringComparison.Ordinal));

            for (int i = 0; i < members.Count; i++)
            {
                var (yamlKey, propType) = members[i];
                if (i > 0) sb.Append(';');
                sb.Append(yamlKey).Append(':').Append(GetTypeName(propType));

                // Recurse into nested [YamlObject] types and their generics
                var elementType = GetYamlElementType(propType);
                if (elementType != null)
                {
                    AppendType(sb, elementType, visited, depth + 1);
                }
            }

            sb.Append('}');
        }

        /// <summary>
        /// Gets a stable type name for fingerprinting. Handles generics like List<T>.
        /// </summary>
        private static string GetTypeName(Type type)
        {
            if (!type.IsGenericType) return type.Name;
            var args = type.GetGenericArguments();
            var baseName = type.Name.Substring(0, type.Name.IndexOf('`'));
            var sb = new StringBuilder(baseName);
            sb.Append('<');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(GetTypeName(args[i]));
            }
            sb.Append('>');
            return sb.ToString();
        }

        /// <summary>
        /// Extracts the element type if it's a [YamlObject] or enum that should be included in fingerprint.
        /// Handles direct types, List<T>, and enums.
        /// </summary>
        private static Type GetYamlElementType(Type type)
        {
            // Direct [YamlObject] type
            if (type.GetCustomAttribute<YamlObjectAttribute>() != null) return type;

            // Enum type
            if (type.IsEnum) return type;

            // List<T> where T is [YamlObject] or enum
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = type.GetGenericArguments()[0];
                if (elementType.GetCustomAttribute<YamlObjectAttribute>() != null) return elementType;
                if (elementType.IsEnum) return elementType;
            }

            return null;
        }
    }
}
