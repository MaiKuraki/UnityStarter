#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CycloneGames.AssetManagement.Editor
{
    /// <summary>
    /// High-performance build-time validator for AssetRef / SceneRef references.
    /// <para>
    /// Architecture (4-phase pipeline):
    /// <list type="number">
    ///   <item>Gather all text-serialised asset paths (main thread, zero-load).</item>
    ///   <item>Parallel file I/O + text scan for <c>m_GUID:</c> / <c>m_Location:</c> pairs — skips 99% of files instantly.</item>
    ///   <item>Parallel GUID resolution via <see cref="AssetDatabase.GUIDToAssetPath"/>.</item>
    ///   <item>SerializedObject write-back only for the handful of files that need healing.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class AssetRefValidator
    {
        // ── Markers in Unity YAML ────────────────────────────────────────────
        // AssetRef serialises as:
        //   someRef:
        //     m_Location: Assets/path/to/asset
        //     m_GUID: a1b2c3d4e5f6...
        // "m_GUID" (capital) is NOT the same as Unity's built-in "guid" (lowercase) used in object references.
        private const string GuidMarker     = "m_GUID: ";
        private const string LocationMarker = "m_Location: ";

        private struct TextRef
        {
            public int    FileIndex;
            public string GUID;
            public string StoredLocation;
        }

        [MenuItem("Tools/CycloneGames/AssetManagement/Validate All AssetRefs")]
        public static void ValidateAll()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // ══════════════════════════════════════════════════════════════
                // Phase 0 — Collect scannable asset paths (main thread, fast)
                // ══════════════════════════════════════════════════════════════
                EditorUtility.DisplayProgressBar("AssetRef Validation", "Collecting asset paths...", 0f);

                var allPaths = AssetDatabase.GetAllAssetPaths();
                var paths    = new List<string>(allPaths.Length / 4);

                for (int i = 0; i < allPaths.Length; i++)
                {
                    var p = allPaths[i];
                    if (!p.StartsWith("Assets/", StringComparison.Ordinal)) continue;

                    // Only text-serialisable types that can contain MonoBehaviour / SO fields
                    if (p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".controller", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".overrideController", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".playable", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".signal", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".lighting", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".mask", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(p);
                    }
                }

                int totalFiles = paths.Count;
                if (totalFiles == 0)
                {
                    Debug.Log("[AssetRef Validation] No scannable assets found.");
                    return;
                }

                // ══════════════════════════════════════════════════════════════
                // Phase 1 — Parallel file read + text scan
                //   • File.ReadAllText is thread-safe and I/O-bound — ideal for Parallel.For
                //   • IndexOf("m_GUID: ") skips files without AssetRef instantly
                //   • No Unity object loading, no SerializedObject, no deserialization
                // ══════════════════════════════════════════════════════════════
                EditorUtility.DisplayProgressBar("AssetRef Validation", $"Scanning {totalFiles} files...", 0.1f);

                // Resolve full disk paths once (needed for File.ReadAllText)
                var diskPaths = new string[totalFiles];
                for (int i = 0; i < totalFiles; i++)
                    diskPaths[i] = Path.GetFullPath(paths[i]);

                // Per-file scan results (null = no AssetRef in this file)
                var resultsPerFile = new List<TextRef>[totalFiles];

                Parallel.For(0, totalFiles, i =>
                {
                    string text;
                    try { text = File.ReadAllText(diskPaths[i]); }
                    catch { return; } // deleted / locked → skip

                    if (text.Length == 0) return;

                    // ── Fast reject: skip files without our marker ──
                    if (text.IndexOf(GuidMarker, StringComparison.Ordinal) < 0) return;

                    var hits = new List<TextRef>(4);
                    int searchFrom = 0;

                    while (searchFrom < text.Length)
                    {
                        int guidPos = text.IndexOf(GuidMarker, searchFrom, StringComparison.Ordinal);
                        if (guidPos < 0) break;

                        // Extract GUID value (to end of line)
                        int valStart = guidPos + GuidMarker.Length;
                        int lineEnd  = text.IndexOf('\n', valStart);
                        if (lineEnd < 0) lineEnd = text.Length;

                        string guid = text.Substring(valStart, lineEnd - valStart).TrimEnd('\r', ' ');
                        searchFrom = lineEnd + 1;

                        if (guid.Length < 8) continue; // not a valid GUID

                        // ── Indentation of m_GUID line ──
                        int guidLineStart = text.LastIndexOf('\n', guidPos);
                        guidLineStart = guidLineStart < 0 ? 0 : guidLineStart + 1;
                        int guidIndent = guidPos - guidLineStart;

                        // ── Find m_Location as an immediate neighbor at the same indent ──
                        // AssetRef has exactly 2 fields, so m_Location must be within 1 line.
                        string location = null;

                        // Check previous line
                        if (guidLineStart > 1)
                        {
                            int prevLineEnd   = guidLineStart - 1; // '\n' before current line
                            int prevLineStart = text.LastIndexOf('\n', prevLineEnd - 1);
                            prevLineStart = prevLineStart < 0 ? 0 : prevLineStart + 1;

                            int prevIndent = -1;
                            int locIdx     = text.IndexOf(LocationMarker, prevLineStart, prevLineEnd - prevLineStart, StringComparison.Ordinal);
                            if (locIdx >= 0) prevIndent = locIdx - prevLineStart;

                            if (locIdx >= 0 && prevIndent == guidIndent)
                            {
                                int locValStart = locIdx + LocationMarker.Length;
                                location = text.Substring(locValStart, prevLineEnd - locValStart).TrimEnd('\r', ' ');
                            }
                        }

                        // Check next line (field order may vary)
                        if (location == null && searchFrom < text.Length)
                        {
                            int nextLineEnd = text.IndexOf('\n', searchFrom);
                            if (nextLineEnd < 0) nextLineEnd = text.Length;

                            int nextIndent = -1;
                            int locIdx     = text.IndexOf(LocationMarker, searchFrom, nextLineEnd - searchFrom, StringComparison.Ordinal);
                            if (locIdx >= 0) nextIndent = locIdx - searchFrom;

                            if (locIdx >= 0 && nextIndent == guidIndent)
                            {
                                int locValStart = locIdx + LocationMarker.Length;
                                location = text.Substring(locValStart, nextLineEnd - locValStart).TrimEnd('\r', ' ');
                            }
                        }

                        // Only record if we found m_Location at the same indent (= real AssetRef struct)
                        if (location == null) continue;

                        hits.Add(new TextRef
                        {
                            FileIndex      = i,
                            GUID           = guid,
                            StoredLocation = location
                        });
                    }

                    if (hits.Count > 0)
                        resultsPerFile[i] = hits;
                });

                // ── Flatten into a contiguous array ──
                int totalRefs = 0;
                for (int i = 0; i < totalFiles; i++)
                    if (resultsPerFile[i] != null) totalRefs += resultsPerFile[i].Count;

                if (totalRefs == 0)
                {
                    sw.Stop();
                    Debug.Log($"[AssetRef Validation] No AssetRef found in {totalFiles} files. ({sw.ElapsedMilliseconds}ms)");
                    return;
                }

                var allRefs = new TextRef[totalRefs];
                int idx = 0;
                for (int i = 0; i < totalFiles; i++)
                {
                    if (resultsPerFile[i] == null) continue;
                    var list = resultsPerFile[i];
                    for (int j = 0; j < list.Count; j++)
                        allRefs[idx++] = list[j];
                }
                resultsPerFile = null; // allow GC

                // ══════════════════════════════════════════════════════════════
                // Phase 2 — GUID resolution (main thread)
                //   AssetDatabase.GUIDToAssetPath is main-thread only in some
                //   Unity versions, so we run a simple loop here.
                //   This is still fast: pure in-memory DB lookup, no disk I/O.
                // ══════════════════════════════════════════════════════════════
                EditorUtility.DisplayProgressBar("AssetRef Validation", $"Resolving {totalRefs} GUIDs...", 0.7f);

                var resolvedPaths = new string[totalRefs];
                for (int i = 0; i < totalRefs; i++)
                    resolvedPaths[i] = AssetDatabase.GUIDToAssetPath(allRefs[i].GUID);

                // ══════════════════════════════════════════════════════════════
                // Phase 3 — Classify + heal
                //   Only files with stale locations need SerializedObject (rare).
                // ══════════════════════════════════════════════════════════════
                EditorUtility.DisplayProgressBar("AssetRef Validation", "Classifying...", 0.85f);

                int broken = 0;
                int healed = 0;
                var errors = new List<string>(16);

                // fileIndex → list of (guid, newLocation)
                var healsPerFile = new Dictionary<int, List<(string guid, string newLocation)>>(8);

                for (int i = 0; i < totalRefs; i++)
                {
                    ref var r = ref allRefs[i];
                    string resolved = resolvedPaths[i];

                    if (string.IsNullOrEmpty(resolved))
                    {
                        broken++;
                        errors.Add($"[BROKEN] {paths[r.FileIndex]} → missing GUID: {r.GUID}");
                    }
                    else if (resolved != r.StoredLocation)
                    {
                        healed++;
                        if (!healsPerFile.TryGetValue(r.FileIndex, out var list))
                        {
                            list = new List<(string, string)>(4);
                            healsPerFile[r.FileIndex] = list;
                        }
                        list.Add((r.GUID, resolved));
                    }
                }

                // ══════════════════════════════════════════════════════════════
                // Phase 4 — Write-back via SerializedObject (only affected files)
                // ══════════════════════════════════════════════════════════════
                if (healsPerFile.Count > 0)
                {
                    EditorUtility.DisplayProgressBar("AssetRef Validation", $"Healing {healsPerFile.Count} file(s)...", 0.95f);

                    foreach (var kvp in healsPerFile)
                    {
                        string assetPath = paths[kvp.Key];
                        var guidToNewLoc = new Dictionary<string, string>(kvp.Value.Count);
                        foreach (var (guid, newLoc) in kvp.Value)
                            guidToNewLoc[guid] = newLoc;

                        var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                        if (assets == null) continue;

                        foreach (var asset in assets)
                        {
                            if (asset == null) continue;
                            var so = new SerializedObject(asset);
                            var it = so.GetIterator();
                            bool modified = false;

                            while (it.NextVisible(true))
                            {
                                if (it.propertyType != SerializedPropertyType.Generic) continue;
                                var gp = it.FindPropertyRelative("m_GUID");
                                var lp = it.FindPropertyRelative("m_Location");
                                if (gp == null || lp == null) continue;
                                if (gp.propertyType != SerializedPropertyType.String) continue;

                                if (guidToNewLoc.TryGetValue(gp.stringValue, out string newLoc) && lp.stringValue != newLoc)
                                {
                                    lp.stringValue = newLoc;
                                    modified = true;
                                }
                            }

                            if (modified)
                                so.ApplyModifiedPropertiesWithoutUndo();
                        }
                    }
                }

                sw.Stop();

                string fileStats = $"{totalFiles} files scanned, {totalRefs} ref(s) found";
                if (broken > 0)
                {
                    Debug.LogError($"[AssetRef Validation] {broken} broken, {healed} healed. ({sw.ElapsedMilliseconds}ms, {fileStats})");
                    foreach (var err in errors)
                        Debug.LogError(err);
                }
                else
                {
                    Debug.Log($"[AssetRef Validation] All valid. {healed} healed. ({sw.ElapsedMilliseconds}ms, {fileStats})");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
