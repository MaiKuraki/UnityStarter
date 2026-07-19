using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Internal correlation identity implemented by built-in provider handles. Editor diagnostics use it to join
    /// tracker and cache rows without relying on non-unique locations, type names, or provider object identity.
    /// </summary>
    internal interface ITrackedAssetHandle
    {
        long DiagnosticHandleId { get; }
    }

    /// <summary>
    /// Thread-safe, bounded diagnostics registry for active asset handles.
    /// </summary>
    public static class HandleTracker
    {
        public const int DEFAULT_CAPACITY = 16_384;
        public const int MAX_CAPACITY = 65_536;

        public struct HandleInfo
        {
            public long Id;
            public string PackageName;
            public string Description;
            public System.DateTime RegistrationTime;
            public System.DateTime ActiveSince;
            internal long ActiveSinceTimestamp;
            public string StackTrace;
        }

        private static readonly object _stateLock = new object();
        private static readonly Dictionary<long, HandleInfo> _activeHandles =
            new Dictionary<long, HandleInfo>();

        private static volatile bool _enabled;
        private static volatile bool _observationIncomplete;
        private static bool _enableStackTrace;
        private static int _capacity = DEFAULT_CAPACITY;
        private static int _pendingRegistrationCount;
        private static int _stateVersion;
        private static long _droppedRegistrationCount;
        private static System.Func<string> _stackTraceCapture = CaptureStackTrace;
        private static System.Func<System.DateTime> _utcNowProvider = GetUtcNow;
        private static System.Func<long> _monotonicTimestampProvider = Stopwatch.GetTimestamp;

        internal static System.Func<string> StackTraceCapture
        {
            get => _stackTraceCapture;
            set => _stackTraceCapture = value ?? throw new System.ArgumentNullException(nameof(value));
        }

        internal static System.Func<System.DateTime> UtcNowProvider
        {
            get => _utcNowProvider;
            set => _utcNowProvider = value ?? throw new System.ArgumentNullException(nameof(value));
        }

        internal static System.Func<long> MonotonicTimestampProvider
        {
            get => _monotonicTimestampProvider;
            set => _monotonicTimestampProvider = value ?? throw new System.ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// True when tracking was enabled after at least one handle could have escaped observation, or when an
        /// enabled registry was cleared. Counts remain exact for the retained registry, not for the process.
        /// </summary>
        internal static bool ObservationIncomplete => _observationIncomplete;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                lock (_stateLock)
                {
                    if (_enabled == value)
                    {
                        return;
                    }

                    _enabled = value;
                    _stateVersion++;
                    if (!value)
                    {
                        _observationIncomplete = true;
                        _activeHandles.Clear();
                        ClearPersistentUnderStateLock();
                    }
                }
            }
        }

        public static bool EnableStackTrace
        {
            get
            {
                lock (_stateLock)
                {
                    return _enableStackTrace;
                }
            }
            set
            {
                lock (_stateLock)
                {
                    _enableStackTrace = value;
                }
            }
        }

        /// <summary>The configured maximum number of active handles retained by the registry.</summary>
        public static int Capacity
        {
            get
            {
                lock (_stateLock)
                {
                    return _capacity;
                }
            }
        }

        /// <summary>
        /// Number of registrations rejected because the configured capacity was exhausted.
        /// The counter is cumulative until the runtime subsystem resets.
        /// </summary>
        public static long DroppedRegistrationCount
        {
            get
            {
                lock (_stateLock)
                {
                    return _droppedRegistrationCount;
                }
            }
        }

        [System.ThreadStatic]
        private static List<HandleInfo> _threadLocalList;

        /// <summary>
        /// Configures the bounded registry before tracking is enabled. Per-handle tracking is intended
        /// for diagnostics; products needing higher cardinality should prefer aggregate telemetry.
        /// </summary>
        public static void ConfigureCapacity(int capacity)
        {
            if (capacity <= 0 || capacity > MAX_CAPACITY)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(capacity),
                    $"Handle tracking capacity must be between 1 and {MAX_CAPACITY}.");
            }

            lock (_stateLock)
            {
                if (_enabled)
                {
                    throw new System.InvalidOperationException(
                        "Handle tracking capacity cannot be changed while tracking is enabled.");
                }

                _capacity = capacity;
            }
        }

        public static void Register(long id, string packageName, string description)
        {
            if (!_enabled) return;

            bool reservedNewEntry;
            bool captureStackTrace;
            int registrationVersion;
            lock (_stateLock)
            {
                if (!_enabled) return;

                reservedNewEntry = !_activeHandles.ContainsKey(id);
                if (reservedNewEntry)
                {
                    if (_activeHandles.Count + _pendingRegistrationCount >= _capacity)
                    {
                        IncrementDroppedRegistrationCount();
                        return;
                    }

                    _pendingRegistrationCount++;
                }

                captureStackTrace = _enableStackTrace;
                registrationVersion = _stateVersion;
            }

            HandleInfo info;
            try
            {
                string stackTrace = null;
                if (captureStackTrace)
                {
                    try
                    {
                        // Keep worker-thread diagnostics independent of Unity API thread-affinity assumptions.
                        stackTrace = _stackTraceCapture();
                    }
                    catch (System.Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
                    {
                        // Diagnostics must never change the outcome or ownership of an asset operation.
                        stackTrace = null;
                    }
                }

                System.DateTime nowUtc = _utcNowProvider();
                long nowTimestamp = _monotonicTimestampProvider();
                info = new HandleInfo
                {
                    Id = id,
                    PackageName = packageName,
                    Description = description,
                    RegistrationTime = nowUtc,
                    ActiveSince = nowUtc,
                    ActiveSinceTimestamp = nowTimestamp,
                    StackTrace = stackTrace
                };
            }
            catch
            {
                CancelPendingRegistration(reservedNewEntry);
                throw;
            }

            lock (_stateLock)
            {
                if (reservedNewEntry)
                {
                    _pendingRegistrationCount--;
                }

                if (!_enabled || registrationVersion != _stateVersion)
                {
                    return;
                }

                if (_activeHandles.ContainsKey(id))
                {
                    _activeHandles[id] = info;
                    return;
                }

                if (!reservedNewEntry &&
                    _activeHandles.Count + _pendingRegistrationCount >= _capacity)
                {
                    IncrementDroppedRegistrationCount();
                    return;
                }

                _activeHandles.Add(id, info);
            }
        }

        /// <summary>
        /// Marks the start of a new active ownership epoch when a cached provider handle moves from idle back to
        /// active. The original registration timestamp remains unchanged for total-lifetime diagnostics.
        /// </summary>
        internal static void MarkActive(long id)
        {
            if (!_enabled || id <= 0L) return;

            System.DateTime nowUtc = _utcNowProvider();
            long nowTimestamp = _monotonicTimestampProvider();
            lock (_stateLock)
            {
                if (!_enabled || !_activeHandles.TryGetValue(id, out HandleInfo info)) return;
                info.ActiveSince = nowUtc;
                info.ActiveSinceTimestamp = nowTimestamp;
                _activeHandles[id] = info;
            }
        }

        internal static long GetMonotonicTimestamp()
        {
            return _monotonicTimestampProvider();
        }

        internal static double GetActiveDurationSeconds(in HandleInfo info, long nowTimestamp)
        {
            long startTimestamp = info.ActiveSinceTimestamp;
            if (startTimestamp <= 0L || nowTimestamp <= startTimestamp) return 0d;
            return (double)(nowTimestamp - startTimestamp) / Stopwatch.Frequency;
        }

        /// <summary>Records that a handle was created while per-handle tracking was disabled.</summary>
        internal static void NotifyHandleCreated()
        {
            if (!_enabled)
            {
                _observationIncomplete = true;
            }
        }

        /// <summary>Marks the current observation epoch incomplete without enabling per-handle tracking.</summary>
        internal static void MarkObservationIncomplete()
        {
            _observationIncomplete = true;
        }

        public static void Unregister(long id)
        {
            if (!_enabled) return;

            bool removed;
            lock (_stateLock)
            {
                if (!_enabled) return;
                removed = _activeHandles.Remove(id);
            }

            if (removed && _hasPersistent)
            {
                UnmarkPersistent(id);
            }
        }

        public static List<HandleInfo> GetActiveHandles()
        {
            var list = _threadLocalList ?? (_threadLocalList = new List<HandleInfo>(64));
            list.Clear();

            lock (_stateLock)
            {
                if (!_enabled) return list;

                foreach (var kvp in _activeHandles)
                {
                    list.Add(kvp.Value);
                }
            }

            return list;
        }

        /// <summary>
        /// Copies at most <paramref name="maxCount"/> entries for bounded Editor diagnostics and returns the
        /// exact active registry count observed under the same lock. The returned count can exceed the number
        /// copied to <paramref name="destination"/>.
        /// </summary>
        internal static int CopyActiveHandlesTo(List<HandleInfo> destination, int maxCount)
        {
            if (destination == null) throw new System.ArgumentNullException(nameof(destination));
            if (maxCount < 0) throw new System.ArgumentOutOfRangeException(nameof(maxCount));

            destination.Clear();
            lock (_stateLock)
            {
                if (!_enabled) return 0;

                int totalCount = _activeHandles.Count;
                if (maxCount == 0) return totalCount;

                foreach (var kvp in _activeHandles)
                {
                    if (destination.Count >= maxCount) break;
                    destination.Add(kvp.Value);
                }

                return totalCount;
            }
        }

        public static int GetActiveHandleCount()
        {
            lock (_stateLock)
            {
                return _enabled ? _activeHandles.Count : 0;
            }
        }

        public static string GetActiveHandlesReport()
        {
            if (!Enabled) return "Handle tracking is disabled.";

            var handles = GetActiveHandles();
            if (handles.Count == 0)
            {
                return ObservationIncomplete
                    ? "No tracked handles; the current observation epoch is incomplete."
                    : "No active handles.";
            }

            var sb = new StringBuilder(handles.Count * 128);
            sb.AppendLine($"--- Active Asset Handles Report ({handles.Count}) ---");
            if (ObservationIncomplete)
            {
                sb.AppendLine("Observation epoch is incomplete; handles created before tracking began may be absent.");
            }
            foreach (var handle in handles)
            {
                sb.AppendLine($"[ID: {handle.Id}] [Package: {handle.PackageName}] [Active Since: {handle.ActiveSince:HH:mm:ss}] - {handle.Description}");
                if (!string.IsNullOrEmpty(handle.StackTrace))
                {
                    sb.AppendLine($"Stack Trace:\n{handle.StackTrace}");
                }
            }
            return sb.ToString();
        }

        public static void Clear()
        {
            lock (_stateLock)
            {
                _stateVersion++;
                if (_enabled)
                {
                    _observationIncomplete = true;
                }
                _activeHandles.Clear();
                ClearPersistentUnderStateLock();
            }
        }

        internal static void Reset()
        {
            lock (_stateLock)
            {
                _enabled = false;
                _enableStackTrace = false;
                _capacity = DEFAULT_CAPACITY;
                _droppedRegistrationCount = 0L;
                _observationIncomplete = false;
                _stackTraceCapture = CaptureStackTrace;
                _utcNowProvider = GetUtcNow;
                _monotonicTimestampProvider = Stopwatch.GetTimestamp;
                _stateVersion++;
                _activeHandles.Clear();
                ClearPersistentUnderStateLock();
            }

            _threadLocalList?.Clear();
        }

        private static void IncrementDroppedRegistrationCount()
        {
            if (_droppedRegistrationCount < long.MaxValue)
            {
                _droppedRegistrationCount++;
            }
        }

        private static void CancelPendingRegistration(bool reservedNewEntry)
        {
            if (!reservedNewEntry)
            {
                return;
            }

            lock (_stateLock)
            {
                _pendingRegistrationCount--;
            }
        }

        private static string CaptureStackTrace()
        {
            return System.Environment.StackTrace;
        }

        private static System.DateTime GetUtcNow()
        {
            return System.DateTime.UtcNow;
        }

        // Intentionally long-lived handle registry. Exact process-wide handle identities avoid cross-package,
        // cross-type, and same-location diagnostic exemptions. Marks are session diagnostics and are reset at
        // subsystem registration; they are not persisted authoring data.
        private static readonly HashSet<long> _persistentHandleIds = new HashSet<long>();
        private static readonly object _persistentLock = new object();
        private static volatile bool _hasPersistent;

        /// <summary>True when at least one tracked handle has been marked intentionally long-lived.</summary>
        public static bool HasPersistentEntries => _hasPersistent;

        /// <summary>
        /// Marks one tracked handle as intentionally long-lived for the current runtime session. Diagnostics
        /// report that exact handle as "Persistent" rather than a leak suspect.
        /// </summary>
        public static void MarkPersistent(long handleId)
        {
            if (handleId <= 0L) return;

            // Lock order is always state -> persistent. This makes validation and insertion atomic with
            // Unregister while keeping the persistent set bounded by the configured tracking capacity.
            lock (_stateLock)
            {
                if (!_enabled || !_activeHandles.ContainsKey(handleId)) return;

                lock (_persistentLock)
                {
                    if (_persistentHandleIds.Add(handleId)) _hasPersistent = true;
                }
            }
        }

        /// <summary>Removes a previously marked intentionally long-lived handle identity.</summary>
        public static void UnmarkPersistent(long handleId)
        {
            if (handleId <= 0L) return;
            lock (_persistentLock)
            {
                _persistentHandleIds.Remove(handleId);
                _hasPersistent = _persistentHandleIds.Count > 0;
            }
        }

        /// <summary>Clears all persistent markings.</summary>
        public static void ClearPersistent()
        {
            lock (_persistentLock)
            {
                ClearPersistentWithoutLock();
            }
        }

        /// <summary>Returns true if the exact tracked handle is marked intentionally long-lived.</summary>
        public static bool IsPersistent(long handleId)
        {
            if (!_hasPersistent || handleId <= 0L) return false;
            lock (_persistentLock)
            {
                return _persistentHandleIds.Contains(handleId);
            }
        }

        // Caller must hold _stateLock. This preserves the only nested lock order used by the tracker:
        // state -> persistent. Public ClearPersistent takes only _persistentLock and never nests state.
        private static void ClearPersistentUnderStateLock()
        {
            lock (_persistentLock)
            {
                ClearPersistentWithoutLock();
            }
        }

        private static void ClearPersistentWithoutLock()
        {
            _persistentHandleIds.Clear();
            _hasPersistent = false;
        }
    }
}
