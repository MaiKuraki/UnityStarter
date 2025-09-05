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
        }

        public static bool Enabled { get; set; }

        private static readonly Dictionary<int, HandleInfo> activeHandles = new Dictionary<int, HandleInfo>();
        private static readonly object lockObject = new object();

        public static void Register(int id, string packageName, string description)
        {
            if (!Enabled) return;

            lock (lockObject)
            {
                var info = new HandleInfo
                {
                    Id = id,
                    PackageName = packageName,
                    Description = description,
                    RegistrationTime = System.DateTime.UtcNow
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
            }
            return sb.ToString();
        }
    }
}