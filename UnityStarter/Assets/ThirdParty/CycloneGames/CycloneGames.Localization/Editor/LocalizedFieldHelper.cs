#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    /// <summary>
    /// Shared helper for LocalizedString / LocalizedAsset property drawers.
    /// Discovers StringTable / AssetTable assets and provides dropdown popups
    /// for table selection and key selection.
    /// <para>
    /// Cache is invalidated only on asset import/delete via <see cref="AssetPostprocessor"/>,
    /// not per-frame. Safe for large projects.
    /// </para>
    /// </summary>
    internal static class LocalizedFieldHelper
    {
        public enum TableType { String, Asset }

        private static int s_Version;
        private static string[] s_StringTableIds;
        private static string[] s_AssetTableIds;
        private static GUIContent[] s_StringTableContents;
        private static GUIContent[] s_AssetTableContents;
        private static int s_StringTableVersion;
        private static int s_AssetTableVersion;

        private static readonly Dictionary<string, CachedKeys> s_StringKeyCache = new(8);
        private static readonly Dictionary<string, CachedKeys> s_AssetKeyCache = new(8);

        // Cached GUIContent for disabled state — allocated once
        private static readonly GUIContent[] s_SelectTableFirst =
            { new GUIContent("Select a table first") };

        private struct CachedKeys
        {
            public int Version;
            public string[] Keys;
            public GUIContent[] Contents;
        }

        /// <summary>
        /// Call from <see cref="AssetPostprocessor"/> or manually to invalidate lookup caches.
        /// </summary>
        public static void InvalidateCache()
        {
            s_Version++;
        }

        // ── Table Popup ─────────────────────────────────────────
        public static void DrawTablePopup(Rect rect, SerializedProperty tableIdProp,
            GUIContent label, TableType type)
        {
            GUIContent[] contents;
            string[] tableIds;

            if (type == TableType.String)
            {
                EnsureStringTableIds();
                tableIds = s_StringTableIds;
                contents = s_StringTableContents;
            }
            else
            {
                EnsureAssetTableIds();
                tableIds = s_AssetTableIds;
                contents = s_AssetTableContents;
            }

            if (tableIds.Length == 0)
            {
                EditorGUI.PropertyField(rect, tableIdProp, label);
                return;
            }

            string current = tableIdProp.stringValue;
            int selectedIdx = Array.IndexOf(tableIds, current);
            int popupIdx = selectedIdx >= 0 ? selectedIdx + 1 : 0;

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUI.Popup(rect, label, popupIdx, contents);
            if (EditorGUI.EndChangeCheck())
            {
                tableIdProp.stringValue = newIdx > 0 ? tableIds[newIdx - 1] : "";
            }
        }

        // ── Key Popups ──────────────────────────────────────────
        public static void DrawStringKeyPopup(Rect rect, SerializedProperty entryKeyProp,
            GUIContent label, string tableId)
        {
            if (string.IsNullOrEmpty(tableId))
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.Popup(rect, label, 0, s_SelectTableFirst);
                EditorGUI.EndDisabledGroup();
                return;
            }

            var cached = GetOrBuildStringKeys(tableId);
            DrawKeyPopupInternal(rect, entryKeyProp, label, cached.Keys, cached.Contents);
        }

        public static void DrawAssetKeyPopup(Rect rect, SerializedProperty entryKeyProp,
            GUIContent label, string tableId)
        {
            if (string.IsNullOrEmpty(tableId))
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.Popup(rect, label, 0, s_SelectTableFirst);
                EditorGUI.EndDisabledGroup();
                return;
            }

            var cached = GetOrBuildAssetKeys(tableId);
            DrawKeyPopupInternal(rect, entryKeyProp, label, cached.Keys, cached.Contents);
        }

        private static void DrawKeyPopupInternal(Rect rect, SerializedProperty entryKeyProp,
            GUIContent label, string[] keys, GUIContent[] contents)
        {
            if (keys.Length == 0)
            {
                EditorGUI.PropertyField(rect, entryKeyProp, label);
                return;
            }

            string current = entryKeyProp.stringValue;
            int selectedIdx = Array.IndexOf(keys, current);
            int popupIdx = selectedIdx >= 0 ? selectedIdx + 1 : 0;

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUI.Popup(rect, label, popupIdx, contents);
            if (EditorGUI.EndChangeCheck())
            {
                entryKeyProp.stringValue = newIdx > 0 ? keys[newIdx - 1] : "";
            }
        }

        // ── Lazy Build with Version Check ───────────────────────
        private static void EnsureStringTableIds()
        {
            if (s_StringTableIds != null && s_StringTableVersion == s_Version) return;
            s_StringTableVersion = s_Version;
            s_StringTableIds = FindTableIds<StringTable>();
            s_StringTableContents = BuildTableContents(s_StringTableIds);
        }

        private static void EnsureAssetTableIds()
        {
            if (s_AssetTableIds != null && s_AssetTableVersion == s_Version) return;
            s_AssetTableVersion = s_Version;
            s_AssetTableIds = FindTableIds<AssetTable>();
            s_AssetTableContents = BuildTableContents(s_AssetTableIds);
        }

        private static CachedKeys GetOrBuildStringKeys(string tableId)
        {
            if (s_StringKeyCache.TryGetValue(tableId, out var cached) && cached.Version == s_Version)
                return cached;

            var keys = FindKeys<StringTable>(tableId);
            cached = new CachedKeys
            {
                Version = s_Version,
                Keys = keys,
                Contents = BuildKeyContents(keys)
            };
            s_StringKeyCache[tableId] = cached;
            return cached;
        }

        private static CachedKeys GetOrBuildAssetKeys(string tableId)
        {
            if (s_AssetKeyCache.TryGetValue(tableId, out var cached) && cached.Version == s_Version)
                return cached;

            var keys = FindKeys<AssetTable>(tableId);
            cached = new CachedKeys
            {
                Version = s_Version,
                Keys = keys,
                Contents = BuildKeyContents(keys)
            };
            s_AssetKeyCache[tableId] = cached;
            return cached;
        }

        // ── Discovery (only runs on cache miss) ─────────────────
        private static string[] FindTableIds<T>() where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids.Length == 0) return Array.Empty<string>();

            var ids = new HashSet<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var table = AssetDatabase.LoadAssetAtPath<T>(path);
                if (table == null) continue;

                // Read tableId via SerializedObject — avoids depending on public property name
                var so = new SerializedObject(table);
                string id = so.FindProperty("tableId").stringValue;
                if (!string.IsNullOrEmpty(id))
                    ids.Add(id);
            }

            var result = new string[ids.Count];
            ids.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static string[] FindKeys<T>(string tableId) where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids.Length == 0) return Array.Empty<string>();

            var keys = new HashSet<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var table = AssetDatabase.LoadAssetAtPath<T>(path);
                if (table == null) continue;

                var so = new SerializedObject(table);
                if (so.FindProperty("tableId").stringValue != tableId) continue;

                var entriesProp = so.FindProperty("entries");
                for (int e = 0; e < entriesProp.arraySize; e++)
                {
                    string key = entriesProp.GetArrayElementAtIndex(e)
                        .FindPropertyRelative("Key").stringValue;
                    if (!string.IsNullOrEmpty(key))
                        keys.Add(key);
                }
            }

            var result = new string[keys.Count];
            keys.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        // ── GUIContent Builders (cached alongside data) ─────────
        private static GUIContent[] BuildTableContents(string[] tableIds)
        {
            var contents = new GUIContent[tableIds.Length + 1];
            contents[0] = new GUIContent("(none)");
            for (int i = 0; i < tableIds.Length; i++)
                contents[i + 1] = new GUIContent(tableIds[i]);
            return contents;
        }

        private static GUIContent[] BuildKeyContents(string[] keys)
        {
            var contents = new GUIContent[keys.Length + 1];
            contents[0] = new GUIContent("(none)");
            for (int i = 0; i < keys.Length; i++)
                contents[i + 1] = new GUIContent(keys[i]);
            return contents;
        }
    }

    /// <summary>
    /// Listens to asset import/delete events to invalidate the localization lookup cache.
    /// </summary>
    internal sealed class LocalizedFieldPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            for (int i = 0; i < importedAssets.Length; i++)
            {
                if (IsTableAsset(importedAssets[i]))
                {
                    LocalizedFieldHelper.InvalidateCache();
                    return;
                }
            }
            for (int i = 0; i < deletedAssets.Length; i++)
            {
                if (IsTableAsset(deletedAssets[i]))
                {
                    LocalizedFieldHelper.InvalidateCache();
                    return;
                }
            }
        }

        private static bool IsTableAsset(string path)
        {
            return path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
