using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Editor
{
    /// <summary>
    /// Scene-view display toggles for trajectory probes.
    ///
    /// These are view preferences, not runtime module configuration. The source of truth is an
    /// explicit, human-readable JSON file at <see cref="SettingsFileRelativePath"/> inside
    /// ProjectSettings, next to the other reviewable project settings. Nothing is stored in
    /// EditorPrefs / PlayerPrefs / SessionState, and no hidden in-memory-only state owns a value:
    /// a change is written to disk immediately, so the current value can always be read, diffed,
    /// reloaded and debugged without opening the Editor.
    ///
    /// Static state here is only a cache of that file. ReloadFromDisk() discards it,
    /// ResetToDefaults() rewrites the file, and the workbench exposes both plus the exact path.
    /// Setters are plain field assignments: no delegates, no lambdas, no per-frame allocation.
    /// </summary>
    public static class TrajectoryGizmoSettings
    {
        /// <summary>Explicit, reviewable location of the settings file, relative to the project root.</summary>
        public const string SettingsFileRelativePath = "ProjectSettings/CycloneGames.TrajectoryGizmoSettings.json";

        [Serializable]
        private sealed class SettingsData
        {
            public bool ShowSegments = true;
            public bool ShowHits = true;
            public bool ShowNormals = true;
            public bool ShowLabels = true;
            public bool ShowHandles = true;
            public bool ShowShapeAtOrigin = true;
            public bool ShowShapeAtEnd = false;
            public bool ShowSweepWalls = false;
            public bool ShowCapsuleCaps = true;
            public bool ShowIdleIndicator = true;
        }

        private static SettingsData _current;
        private static string _lastLoadError;

        /// <summary>Absolute path of the settings file. Displayed in the workbench for debugging.</summary>
        public static string SettingsFilePath
        {
            get
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.GetFullPath(Path.Combine(projectRoot, SettingsFileRelativePath));
            }
        }

        /// <summary>Last file error, or null. Surfaced as a HelpBox so a bad file is never silent.</summary>
        public static string LastLoadError
        {
            get { return _lastLoadError; }
        }

        public static bool IsLoadedFromFile
        {
            get { return _current != null; }
        }

        private static SettingsData Current
        {
            get
            {
                if (_current == null)
                {
                    ReloadFromDisk();
                }

                return _current;
            }
        }

        /// <summary>Discards the in-memory cache and re-reads the explicit settings file.</summary>
        public static void ReloadFromDisk()
        {
            _lastLoadError = null;
            string path = SettingsFilePath;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    SettingsData loaded = JsonUtility.FromJson<SettingsData>(json);
                    _current = loaded == null ? CreateDefault() : loaded;
                }
                else
                {
                    // Create a discoverable file on first use instead of silently keeping an
                    // invisible default in memory.
                    _current = CreateDefault();
                    WriteToDisk(_current);
                }
            }
            catch (Exception error)
            {
                _lastLoadError = error.Message;
                _current = CreateDefault();
            }
        }

        /// <summary>Overwrites the settings file with the documented defaults.</summary>
        public static void ResetToDefaults()
        {
            _current = CreateDefault();
            WriteToDisk(_current);
        }

        private static SettingsData CreateDefault()
        {
            return new SettingsData();
        }

        private static void WriteToDisk(SettingsData data)
        {
            try
            {
                string path = SettingsFilePath;
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, JsonUtility.ToJson(data, true));
                _lastLoadError = null;
            }
            catch (Exception error)
            {
                _lastLoadError = error.Message;
            }
        }

        public static bool ShowSegments
        {
            get { return Current.ShowSegments; }
            set
            {
                if (Current.ShowSegments == value) return;
                Current.ShowSegments = value;
                WriteToDisk(Current);
            }
        }

        public static bool ShowHits
        {
            get { return Current.ShowHits; }
            set
            {
                if (Current.ShowHits == value) return;
                Current.ShowHits = value;
                WriteToDisk(Current);
            }
        }

        public static bool ShowNormals
        {
            get { return Current.ShowNormals; }
            set
            {
                if (Current.ShowNormals == value) return;
                Current.ShowNormals = value;
                WriteToDisk(Current);
            }
        }

        public static bool ShowLabels
        {
            get { return Current.ShowLabels; }
            set
            {
                if (Current.ShowLabels == value) return;
                Current.ShowLabels = value;
                WriteToDisk(Current);
            }
        }

        public static bool ShowHandles
        {
            get { return Current.ShowHandles; }
            set
            {
                if (Current.ShowHandles == value) return;
                Current.ShowHandles = value;
                WriteToDisk(Current);
            }
        }

        /// <summary>Wireframe of the sweep volume at the trace origin.</summary>
        public static bool ShowShapeAtOrigin
        {
            get { return Current.ShowShapeAtOrigin; }
            set
            {
                if (Current.ShowShapeAtOrigin == value) return;
                Current.ShowShapeAtOrigin = value;
                WriteToDisk(Current);
            }
        }

        /// <summary>Wireframe of the sweep volume at the final position. Off by default: a multi-bounce
        /// trace would otherwise fill the view with boxes.</summary>
        public static bool ShowShapeAtEnd
        {
            get { return Current.ShowShapeAtEnd; }
            set
            {
                if (Current.ShowShapeAtEnd == value) return;
                Current.ShowShapeAtEnd = value;
                WriteToDisk(Current);
            }
        }

        /// <summary>
        /// Lines connecting the origin shape to the end shape, forming the swept tube. Off by default:
        /// the end shape sits up to MaxDistance away, so these read as four long diagonals crossing
        /// the whole scene rather than as a tube.
        /// </summary>
        public static bool ShowSweepWalls
        {
            get { return Current.ShowSweepWalls; }
            set
            {
                if (Current.ShowSweepWalls == value) return;
                Current.ShowSweepWalls = value;
                WriteToDisk(Current);
            }
        }

        /// <summary>
        /// Draws hemispherical meridian arcs on capsule caps. Without them the wireframe reads as a
        /// cylinder, which is the wrong volume for a CapsuleCast, so this is on by default. The cap
        /// poles are drawn with the arcs and never on their own.
        /// </summary>
        public static bool ShowCapsuleCaps
        {
            get { return Current.ShowCapsuleCaps; }
            set
            {
                if (Current.ShowCapsuleCaps == value) return;
                Current.ShowCapsuleCaps = value;
                WriteToDisk(Current);
            }
        }

        /// <summary>Dimmed indicator line drawn for probes that are not selected.</summary>
        public static bool ShowIdleIndicator
        {
            get { return Current.ShowIdleIndicator; }
            set
            {
                if (Current.ShowIdleIndicator == value) return;
                Current.ShowIdleIndicator = value;
                WriteToDisk(Current);
            }
        }

        public static void DrawMenuItems()
        {
            ShowSegments = EditorGUILayout.ToggleLeft("Segments", ShowSegments);
            ShowHits = EditorGUILayout.ToggleLeft("Hit points", ShowHits);
            ShowNormals = EditorGUILayout.ToggleLeft("Hit normals", ShowNormals);
            ShowLabels = EditorGUILayout.ToggleLeft("Labels", ShowLabels);
            ShowHandles = EditorGUILayout.ToggleLeft("Scene handles", ShowHandles);
            ShowShapeAtOrigin = EditorGUILayout.ToggleLeft("Sweep shape at origin", ShowShapeAtOrigin);
            ShowShapeAtEnd = EditorGUILayout.ToggleLeft("Sweep shape at end", ShowShapeAtEnd);
            ShowSweepWalls = EditorGUILayout.ToggleLeft("Sweep walls (connect shapes)", ShowSweepWalls);
            ShowCapsuleCaps = EditorGUILayout.ToggleLeft("Capsule cap arcs", ShowCapsuleCaps);
            ShowIdleIndicator = EditorGUILayout.ToggleLeft("Idle indicator (unselected)", ShowIdleIndicator);
        }
    }
}
