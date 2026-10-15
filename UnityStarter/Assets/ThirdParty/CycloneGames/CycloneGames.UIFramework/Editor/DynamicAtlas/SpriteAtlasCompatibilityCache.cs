using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace CycloneGames.UIFramework.Editor.DynamicAtlas
{
    /// <summary>
    /// Project-wide SpriteAtlas compatibility cache fed by import events. Scan requests are
    /// throttled: repeated triggers inside the delay window merge into one deferred scan, and a
    /// global switch can disable automatic scanning entirely. The decision logic takes time as a
    /// parameter so it stays testable without importing real assets.
    /// </summary>
    internal static class SpriteAtlasCompatibilityCache
    {
        internal readonly struct Entry
        {
            internal readonly string Path;
            internal readonly string Name;
            internal readonly string FormatName;
            internal readonly bool IsExplicitRgba32;
            internal readonly bool Readable;
            internal readonly bool RotationEnabled;
            internal readonly bool TightPackingEnabled;
            internal readonly SpriteAtlasFormatValidator.Compatibility Compatibility;

            internal Entry(
                string path,
                string name,
                string formatName,
                bool isExplicitRgba32,
                bool readable,
                bool rotationEnabled,
                bool tightPackingEnabled,
                SpriteAtlasFormatValidator.Compatibility compatibility)
            {
                Path = path;
                Name = name;
                FormatName = formatName;
                IsExplicitRgba32 = isExplicitRgba32;
                Readable = readable;
                RotationEnabled = rotationEnabled;
                TightPackingEnabled = tightPackingEnabled;
                Compatibility = compatibility;
            }
        }

        private const double DefaultScanDelaySeconds = 0.5d;

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly List<Entry> ScanBuffer = new List<Entry>(64);

        private static Sprite[] _spriteBuffer = new Sprite[1];
        private static double _pendingSince = -1d;

        internal static bool AutoScanEnabled = true;
        internal static double ScanDelaySeconds = DefaultScanDelaySeconds;
        internal static int CompletedScanCount { get; private set; }
        internal static double LastScanTime { get; private set; } = -1d;

        internal static event Action ScanCompleted;

        internal static int Count => Entries.Count;
        internal static bool HasPendingScan => _pendingSince >= 0d;

        // Returns true only for the first request of a burst; later requests merge into the
        // already pending scan, so the caller registers at most one delayed pump per burst.
        internal static bool RequestScan(double now)
        {
            if (!AutoScanEnabled)
            {
                _pendingSince = -1d;
                return false;
            }

            if (_pendingSince >= 0d)
            {
                return false;
            }

            _pendingSince = now;
            return true;
        }

        // Returns true once the delay has elapsed; consumes the pending request so a burst of
        // import events collapses into a single scan.
        internal static bool TryConsumePendingScan(double now)
        {
            if (!AutoScanEnabled)
            {
                _pendingSince = -1d;
                return false;
            }

            if (_pendingSince < 0d || now - _pendingSince < ScanDelaySeconds)
            {
                return false;
            }

            _pendingSince = -1d;
            return true;
        }

        internal static bool TryGet(string path, out Entry entry)
        {
            return Entries.TryGetValue(path, out entry);
        }

        internal static int CopyEntries(List<Entry> destination)
        {
            destination.Clear();
            foreach (KeyValuePair<string, Entry> pair in Entries)
            {
                destination.Add(pair.Value);
            }

            destination.Sort(CompareBySeverityThenPath);
            return destination.Count;
        }

        internal static int CopyVisible(List<Entry> sorted, List<Entry> visible, int maximumVisible)
        {
            visible.Clear();
            int shown = Math.Min(sorted.Count, maximumVisible);
            for (int i = 0; i < shown; i++)
            {
                visible.Add(sorted[i]);
            }

            return sorted.Count - shown;
        }

        internal static int CompareBySeverityThenPath(Entry a, Entry b)
        {
            int bySeverity = (int)b.Compatibility - (int)a.Compatibility;
            return bySeverity != 0 ? bySeverity : string.CompareOrdinal(a.Path, b.Path);
        }

        internal static string BuildFailureReasons(in Entry entry)
        {
            if (entry.Compatibility == SpriteAtlasFormatValidator.Compatibility.FastPathCandidate)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(96);
            if (entry.RotationEnabled)
            {
                AppendReason(builder, "rotation enabled");
            }

            if (entry.TightPackingEnabled)
            {
                AppendReason(builder, "tight packing");
            }

            if (!entry.IsExplicitRgba32)
            {
                AppendReason(builder, "compressed format (" + entry.FormatName + ")");
            }

            if (!entry.Readable)
            {
                AppendReason(builder, "not readable");
            }

            return builder.ToString();
        }

        internal static void ReplaceAll(List<Entry> results)
        {
            Entries.Clear();
            for (int i = 0; i < results.Count; i++)
            {
                Entries[results[i].Path] = results[i];
            }
        }

        internal static int ScanProjectNow()
        {
            ScanBuffer.Clear();
            BuildTarget platform = EditorUserBuildSettings.activeBuildTarget;
            string[] guids = AssetDatabase.FindAssets("t:SpriteAtlas");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas != null)
                {
                    ScanBuffer.Add(BuildEntry(atlas, path, platform));
                }
            }

            ReplaceAll(ScanBuffer);
            ScanBuffer.Clear();
            CompletedScanCount++;
            LastScanTime = EditorApplication.timeSinceStartup;
            ScanCompleted?.Invoke();
            return Entries.Count;
        }

        internal static void ResetForTests()
        {
            Entries.Clear();
            ScanBuffer.Clear();
            Array.Clear(_spriteBuffer, 0, _spriteBuffer.Length);
            _pendingSince = -1d;
            AutoScanEnabled = true;
            ScanDelaySeconds = DefaultScanDelaySeconds;
            CompletedScanCount = 0;
            LastScanTime = -1d;
            ScanCompleted = null;
        }

        private static Entry BuildEntry(SpriteAtlas atlas, string path, BuildTarget platform)
        {
            SpriteAtlasFormatValidator.GetImporterSettings(
                path,
                out bool rotationEnabled,
                out bool tightPackingEnabled,
                out bool readable);
            SpriteAtlasFormatValidator.GetAtlasFormat(
                atlas,
                platform,
                ref _spriteBuffer,
                out string formatName,
                out bool isExplicitRgba32);
            SpriteAtlasFormatValidator.Compatibility compatibility =
                SpriteAtlasFormatValidator.ClassifyCompatibility(
                    rotationEnabled,
                    tightPackingEnabled,
                    isExplicitRgba32,
                    readable);

            return new Entry(
                path,
                atlas.name,
                formatName,
                isExplicitRgba32,
                readable,
                rotationEnabled,
                tightPackingEnabled,
                compatibility);
        }

        private static void AppendReason(System.Text.StringBuilder builder, string reason)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(reason);
        }
    }
}
