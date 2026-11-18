using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    /// <summary>
    /// Owns the constant reflection cache for <see cref="StringAsConstSelectorAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Entries are invalidated after assembly reload and on Play Mode transitions so edits to
    /// a constants class become visible even when Enter Play Mode Options disables Domain Reload.
    /// The cache is bounded: once the type budget is exhausted, the whole cache is rebuilt.
    /// Editor-only and main-thread confined; callers must not mutate the returned data.
    /// </remarks>
    [InitializeOnLoad]
    internal static class StringAsConstSelectorCache
    {
        private const int MaxCachedTypes = 64;
        private const int MaxConstantFields = 4096;

        private static readonly Dictionary<Type, CachedConstantData> Cache =
            new Dictionary<Type, CachedConstantData>(MaxCachedTypes);

        internal static readonly GUIContent NoneContent = new GUIContent("None");

        static StringAsConstSelectorCache()
        {
            AssemblyReloadEvents.afterAssemblyReload -= Clear;
            AssemblyReloadEvents.afterAssemblyReload += Clear;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static CachedConstantData Get(Type constantsType)
        {
            if (constantsType == null)
            {
                return CachedConstantData.MissingType;
            }

            if (Cache.TryGetValue(constantsType, out CachedConstantData cachedData))
            {
                return cachedData;
            }

            cachedData = Build(constantsType);
            if (Cache.Count >= MaxCachedTypes)
            {
                Cache.Clear();
            }

            Cache.Add(constantsType, cachedData);
            return cachedData;
        }

        private static void Clear()
        {
            Cache.Clear();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode ||
                change == PlayModeStateChange.EnteredEditMode)
            {
                Clear();
            }
        }

        private static CachedConstantData Build(Type constantsType)
        {
            try
            {
                FieldInfo[] fields = constantsType.GetFields(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (fields.Length > MaxConstantFields)
                {
                    return CachedConstantData.CreateError(string.Concat(
                        "The constants type declares too many fields: ",
                        constantsType.FullName,
                        "."));
                }

                List<ReflectedConstant> reflectedConstants = new List<ReflectedConstant>(fields.Length);
                for (int index = 0; index < fields.Length; index++)
                {
                    FieldInfo field = fields[index];
                    if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                    {
                        continue;
                    }

                    string value = field.GetRawConstantValue() as string;
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    reflectedConstants.Add(new ReflectedConstant(
                        field.Name,
                        value,
                        field.DeclaringType == null ? string.Empty : field.DeclaringType.FullName));
                }

                reflectedConstants.Sort(ReflectedConstantComparer.Instance);

                List<ReflectedConstant> uniqueConstants =
                    new List<ReflectedConstant>(reflectedConstants.Count);
                HashSet<string> uniqueValues = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < reflectedConstants.Count; index++)
                {
                    ReflectedConstant reflectedConstant = reflectedConstants[index];
                    if (uniqueValues.Add(reflectedConstant.Value))
                    {
                        uniqueConstants.Add(reflectedConstant);
                    }
                }

                if (uniqueConstants.Count == 0)
                {
                    return CachedConstantData.CreateError(string.Concat(
                        "No non-empty public const string values were found in ",
                        constantsType.FullName,
                        "."));
                }

                return new CachedConstantData(uniqueConstants);
            }
            catch (Exception exception)
            {
                return CachedConstantData.CreateError(string.Concat(
                    "Failed to read constants from ",
                    constantsType.FullName,
                    " (",
                    exception.GetType().Name,
                    ")."));
            }
        }

        internal readonly struct ReflectedConstant
        {
            public readonly string DisplayName;
            public readonly string Value;
            public readonly string DeclaringTypeName;

            public ReflectedConstant(string displayName, string value, string declaringTypeName)
            {
                DisplayName = displayName;
                Value = value;
                DeclaringTypeName = declaringTypeName;
            }
        }

        internal sealed class ReflectedConstantComparer : IComparer<ReflectedConstant>
        {
            public static readonly ReflectedConstantComparer Instance = new ReflectedConstantComparer();

            private ReflectedConstantComparer()
            {
            }

            public int Compare(ReflectedConstant x, ReflectedConstant y)
            {
                int nameComparison = string.Compare(x.DisplayName, y.DisplayName, StringComparison.Ordinal);
                if (nameComparison != 0)
                {
                    return nameComparison;
                }

                return string.Compare(x.DeclaringTypeName, y.DeclaringTypeName, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Immutable-per-entry snapshot of one constants type. Built once on a cold path.
        /// </summary>
        internal sealed class CachedConstantData
        {
            public static readonly CachedConstantData MissingType =
                CreateError("The constants type is not configured.");

            public readonly GUIContent[] DisplayOptions;
            public readonly GUIContent[] PopupDisplayOptions;
            public readonly string[] ValueOptions;
            public readonly Dictionary<string, int> ValueToIndexMap;
            public readonly GUIContent ErrorContent;

            private readonly string[] _displayNames;
            private readonly Dictionary<char, CachedMenuData> _menuDataBySeparator;

            public CachedConstantData(List<ReflectedConstant> constants)
            {
                int count = constants.Count;
                _displayNames = new string[count];
                DisplayOptions = new GUIContent[count];
                PopupDisplayOptions = new GUIContent[count + 1];
                PopupDisplayOptions[0] = NoneContent;
                ValueOptions = new string[count];
                ValueToIndexMap = new Dictionary<string, int>(count, StringComparer.Ordinal);
                _menuDataBySeparator = new Dictionary<char, CachedMenuData>();

                for (int index = 0; index < count; index++)
                {
                    ReflectedConstant reflectedConstant = constants[index];
                    _displayNames[index] = reflectedConstant.DisplayName;
                    DisplayOptions[index] = new GUIContent(reflectedConstant.DisplayName);
                    PopupDisplayOptions[index + 1] = DisplayOptions[index];
                    ValueOptions[index] = reflectedConstant.Value;
                    ValueToIndexMap.Add(reflectedConstant.Value, index);
                }
            }

            private CachedConstantData(GUIContent errorContent)
            {
                _displayNames = Array.Empty<string>();
                DisplayOptions = Array.Empty<GUIContent>();
                PopupDisplayOptions = Array.Empty<GUIContent>();
                ValueOptions = Array.Empty<string>();
                ValueToIndexMap = new Dictionary<string, int>(0, StringComparer.Ordinal);
                _menuDataBySeparator = new Dictionary<char, CachedMenuData>();
                ErrorContent = errorContent;
            }

            public static CachedConstantData CreateError(string message)
            {
                return new CachedConstantData(new GUIContent(message));
            }

            public CachedMenuData GetMenuData(char separator)
            {
                if (_menuDataBySeparator.TryGetValue(separator, out CachedMenuData menuData))
                {
                    return menuData;
                }

                GUIContent[] optionContents = new GUIContent[_displayNames.Length];
                for (int index = 0; index < _displayNames.Length; index++)
                {
                    string menuPath = _displayNames[index].Replace(separator, '/');
                    optionContents[index] = new GUIContent(menuPath);
                }

                menuData = new CachedMenuData(optionContents);
                _menuDataBySeparator.Add(separator, menuData);
                return menuData;
            }
        }

        internal sealed class CachedMenuData
        {
            public readonly GUIContent[] OptionContents;

            public CachedMenuData(GUIContent[] optionContents)
            {
                OptionContents = optionContents;
            }
        }
    }
}
