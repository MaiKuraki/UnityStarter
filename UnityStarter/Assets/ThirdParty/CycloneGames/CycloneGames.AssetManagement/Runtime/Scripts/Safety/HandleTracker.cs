using System.Collections.Generic;
using System.Text;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// A utility for tracking active asset handles for diagnostic purposes.
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

        private static readonly Dictionary<int, HandleInfo> activeHandles = new Dictionary<int, HandleInfo>();
        private static readonly object lockObject = new object();

        public static void Register(int id, string packageName, string description)
        {
            if (!Enabled) return;

            string stackTrace = null;
            if (EnableStackTrace)
            {
                stackTrace = UnityEngine.StackTraceUtility.ExtractStackTrace();
            }

            lock (lockObject)
            {
                var info = new HandleInfo
                {
                    Id = id,
                    PackageName = packageName,
                    Description = description,
                    RegistrationTime = System.DateTime.UtcNow,
                    StackTrace = stackTrace
                };
                activeHandles[id] = info;
            }
        }

        public static void Unregister(int id)
        {
            if (!Enabled) return;

            lock (lockObject)
            {
                activeHandles.Remove(id);
            }
        }

        public static List<HandleInfo> GetActiveHandles()
        {
            var handles = new List<HandleInfo>();
            if (!Enabled) return handles;

            lock (lockObject)
            {
                foreach (var kvp in activeHandles)
                {
                    handles.Add(kvp.Value);
                }
            }
            return handles;
        }

        public static string GetActiveHandlesReport()
        {
            if (!Enabled) return "Handle tracking is disabled.";

            var handles = GetActiveHandles();
            if (handles.Count == 0)
            {
                return "No active handles.";
            }

            var sb = new StringBuilder();
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
    }
}