using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Performance profiler for input system operations. Tracks timing for diagnostics.
    /// </summary>
    public static class InputPerformanceProfiler
    {
        private static readonly Dictionary<string, ProfilerEntry> _profiles = new();
        private static bool _enabled = false;

        /// <summary>
        /// When disabled, all profiling calls are no-ops with minimal overhead.
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public static ProfilerScope BeginScope(string operationName)
        {
            if (!_enabled) return default;
            return new ProfilerScope(operationName);
        }

        public static void RecordOperation(string operationName, float milliseconds)
        {
            if (!_enabled) return;

            if (!_profiles.TryGetValue(operationName, out var entry))
            {
                entry = new ProfilerEntry { Name = operationName };
                _profiles[operationName] = entry;
            }

            entry.Count++;
            entry.TotalTime += milliseconds;
            entry.MinTime = Mathf.Min(entry.MinTime, milliseconds);
            entry.MaxTime = Mathf.Max(entry.MaxTime, milliseconds);
        }

        public static string GetStatistics()
        {
            if (_profiles.Count == 0) return "No profiling data available.";

            var sb = new StringBuilder();
            sb.AppendLine("=== Input System Performance Statistics ===");
            sb.AppendLine();

            foreach (var kvp in _profiles)
            {
                var entry = kvp.Value;
                float avgTime = entry.Count > 0 ? entry.TotalTime / entry.Count : 0f;
                sb.AppendLine($"{entry.Name}:");
                sb.AppendLine($"  Count: {entry.Count}");
                sb.AppendLine($"  Total: {entry.TotalTime:F3}ms");
                sb.AppendLine($"  Average: {avgTime:F3}ms");
                sb.AppendLine($"  Min: {entry.MinTime:F3}ms");
                sb.AppendLine($"  Max: {entry.MaxTime:F3}ms");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static void Clear() => _profiles.Clear();
        public static void Reset(string operationName) => _profiles.Remove(operationName);

        private class ProfilerEntry
        {
            public string Name;
            public int Count;
            public float TotalTime;
            public float MinTime = float.MaxValue;
            public float MaxTime = float.MinValue;
        }

        /// <summary>
        /// RAII-style scope that automatically records timing on dispose.
        /// </summary>
        public struct ProfilerScope : IDisposable
        {
            private readonly string _operationName;
            private readonly long _startTicks;
            private readonly bool _isValid;

            internal ProfilerScope(string operationName)
            {
                _operationName = operationName;
                _startTicks = Stopwatch.GetTimestamp();
                _isValid = true;
            }

            public void Dispose()
            {
                if (!_isValid || !_enabled) return;

                long endTicks = Stopwatch.GetTimestamp();
                long elapsedTicks = endTicks - _startTicks;
                double elapsedMs = (elapsedTicks * 1000.0) / Stopwatch.Frequency;
                RecordOperation(_operationName, (float)elapsedMs);
            }
        }
    }
}