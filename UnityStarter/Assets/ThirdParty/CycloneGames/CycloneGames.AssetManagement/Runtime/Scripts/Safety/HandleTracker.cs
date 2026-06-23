using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Lock-free utility for tracking active asset handles for diagnostic purposes.
    /// </summary>
    public static class HandleTracker
    {
        public struct HandleInfo
        {
            public int Id;
            public string PackageName;
            public string Description;
            public System.DateTime RegistrationTime;
            public string StackTrace;
        }

        public static bool Enabled { get; set; }
        public static bool EnableStackTrace { get; set; }

        private static readonly ConcurrentDictionary<int, HandleInfo> _activeHandles = new ConcurrentDictionary<int, HandleInfo>();

        [System.ThreadStatic]
        private static List<HandleInfo> _threadLocalList;

        public static void Register(int id, string packageName, string description)
        {
            if (!Enabled) return;

            string stackTrace = null;
            if (EnableStackTrace)
            {
                stackTrace = UnityEngine.StackTraceUtility.ExtractStackTrace();
            }

            var info = new HandleInfo
            {
                Id = id,
                PackageName = packageName,
                Description = description,
                RegistrationTime = System.DateTime.UtcNow,
                StackTrace = stackTrace
            };
            _activeHandles[id] = info;
        }

        public static void Unregister(int id)
        {
            if (!Enabled) return;
            _activeHandles.TryRemove(id, out _);
        }

        public static List<HandleInfo> GetActiveHandles()
        {
            var list = _threadLocalList ?? (_threadLocalList = new List<HandleInfo>(64));
            list.Clear();

            if (!Enabled) return list;

            foreach (var kvp in _activeHandles)
            {
                list.Add(kvp.Value);
            }
            return list;
        }

        public static int GetActiveHandleCount()
        {
            return Enabled ? _activeHandles.Count : 0;
        }

        public static string GetActiveHandlesReport()
        {
            if (!Enabled) return "Handle tracking is disabled.";

            var handles = GetActiveHandles();
            if (handles.Count == 0)
            {
                return "No active handles.";
            }

            var sb = new StringBuilder(handles.Count * 128);
            sb.AppendLine($"--- Active Asset Handles Report ({handles.Count}) ---");
            foreach (var handle in handles)
            {
                sb.AppendLine($"[ID: {handle.Id}] [Package: {handle.PackageName}] [Time: {handle.RegistrationTime:HH:mm:ss}] - {handle.Description}");
                if (!string.IsNullOrEmpty(handle.StackTrace))
                {
                    sb.AppendLine($"Stack Trace:\n{handle.StackTrace}");
                }
            }
            return sb.ToString();
        }

        public static void Clear()
        {
            _activeHandles.Clear();
        }

        // ── Persistent (intentionally long-lived) registry ───────────────────────
        // Locations marked persistent are excluded from leak heuristics — e.g. DontDestroyOnLoad
        // infrastructure, bootstrap UI, and the main scene's always-resident assets. The registry is
        // independent of <see cref="Enabled"/> and is safe to call from any thread.
        private static readonly HashSet<string> _persistentLocations = new HashSet<string>(System.StringComparer.Ordinal);
        private static readonly object _persistentLock = new object();
        private static volatile bool _hasPersistent;

        /// <summary>True when at least one location has been marked persistent.</summary>
        public static bool HasPersistentEntries => _hasPersistent;

        /// <summary>
        /// Marks an asset location as intentionally long-lived. Diagnostics report its handles as
        /// "Persistent" rather than leak suspects. Safe to call before or after the asset is loaded.
        /// </summary>
        public static void MarkPersistent(string location)
        {
            if (string.IsNullOrEmpty(location)) return;
            lock (_persistentLock)
            {
                if (_persistentLocations.Add(location)) _hasPersistent = true;
            }
        }

        /// <summary>Removes a previously marked persistent location.</summary>
        public static void UnmarkPersistent(string location)
        {
            if (string.IsNullOrEmpty(location)) return;
            lock (_persistentLock)
            {
                _persistentLocations.Remove(location);
                _hasPersistent = _persistentLocations.Count > 0;
            }
        }

        /// <summary>Clears all persistent markings.</summary>
        public static void ClearPersistent()
        {
            lock (_persistentLock)
            {
                _persistentLocations.Clear();
                _hasPersistent = false;
            }
        }

        /// <summary>Returns true if the given location has been marked persistent.</summary>
        public static bool IsPersistent(string location)
        {
            if (!_hasPersistent || string.IsNullOrEmpty(location)) return false;
            lock (_persistentLock)
            {
                return _persistentLocations.Contains(location);
            }
        }
    }
}