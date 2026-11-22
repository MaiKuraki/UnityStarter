using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CycloneGames.Hash.Core;
using CycloneGames.Networking.Replication;

namespace CycloneGames.Networking.Maintenance
{
    public enum NetworkMaintenanceSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public readonly struct NetworkMaintenanceIssue
    {
        public readonly NetworkMaintenanceSeverity Severity;
        public readonly string Code;
        public readonly string Message;
        public readonly string Action;
        // Dynamic context is carried as fields, not concatenated into Message,
        // so Evaluate() stays allocation-free even for diagnostics runs.
        public readonly string TargetId;
        public readonly int Detail;

        public NetworkMaintenanceIssue(
            NetworkMaintenanceSeverity severity,
            string code,
            string message,
            string action,
            string targetId,
            int detail)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Action = action ?? string.Empty;
            TargetId = targetId ?? string.Empty;
            Detail = detail;
        }
    }

    /// <summary>
    /// A periodic reclamation target (e.g. PollEvents, PruneExpired). The host
    /// owns the time semantics of its own implementation; the coordinator only
    /// decides WHEN to call it, using the unified modular tick contract.
    /// </summary>
    public interface INetworkPeriodicMaintenanceTarget
    {
        string MaintenanceId { get; }
        int MaintenanceIntervalTicks { get; }
        void RunPeriodicMaintenance(int serverTick);
    }

    /// <summary>A connection-scoped reclamation target (e.g. RemoveConnection).</summary>
    public interface INetworkConnectionReleaseTarget
    {
        string MaintenanceId { get; }
        void ReleaseConnection(int connectionId);
    }

    /// <summary>
    /// Immutable default coverage checklist. These are the long-lived containers
    /// that do NOT reclaim themselves; a product that omits one leaks state.
    /// Exposed as read-only collections so callers cannot mutate the shared list.
    /// </summary>
    public static class NetworkMaintenanceRequirements
    {
        private static readonly ReadOnlyCollection<string> s_periodic =
            new ReadOnlyCollection<string>(new[]
            {
                "transport.poll",
                "ratelimiter.prune",
                "security.replay"
            });

        private static readonly ReadOnlyCollection<string> s_release =
            new ReadOnlyCollection<string>(new[]
            {
                "statecache.release",
                "ratelimiter.release",
                "security.release",
                "ackstore.release"
            });

        private static readonly ReadOnlyCollection<string> s_teardown =
            new ReadOnlyCollection<string>(new[]
            {
                "gas.identity.teardown",
                "gas.state.teardown",
                "gas.replica.teardown",
                "gf.damage.teardown",
                "interaction.replay.teardown",
                "movement.history.teardown",
                "movement.audit.teardown",
                "projectile.history.teardown",
                "ackstore.teardown",
                "session.roster.teardown"
            });

        public static IReadOnlyList<string> RequiredPeriodicIds => s_periodic;

        public static IReadOnlyList<string> RequiredReleaseIds => s_release;

        public static IReadOnlyList<string> RequiredTeardownIds => s_teardown;

        public static int RequiredCount =>
            s_periodic.Count + s_release.Count + s_teardown.Count;
    }

    /// <summary>
    /// Instance-scoped, allocation-free coordinator that collapses N periodic
    /// reclamation entry points into one Poll(serverTick) and N connection-scoped
    /// entry points into one ReleaseConnection(connectionId).
    ///
    /// Scheduling uses NetworkTickMath, so it stays correct across the
    /// int.MaxValue wrap. Evaluate() is a pure, machine-checkable coverage
    /// self-check whose logic lives entirely in Core; the Editor diagnostics
    /// layer only renders it.
    ///
    /// Single owner thread, no locks. Target arrays and the last-run array are
    /// allocated once at construction; Poll / ReleaseConnection / Evaluate
    /// allocate nothing in the steady state. Products register thin targets for
    /// their own components; periodic time semantics differ between wall time,
    /// monotonic ticks and event-driven handlers.
    /// </summary>
    public sealed class NetworkMaintenanceCoordinator
    {
        private const int DefaultMaxTargets = 16;
        private const int OverdueIntervalMultiplier = 2;

        private readonly INetworkPeriodicMaintenanceTarget[] _periodic;
        private readonly INetworkConnectionReleaseTarget[] _release;
        private readonly INetworkSessionTeardownTarget[] _teardown;
        private readonly int[] _lastRunTick;
        private readonly bool[] _hasRun;

        private int _periodicCount;
        private int _releaseCount;
        private int _teardownCount;
        private int _currentTick;
        private long _totalPollCalls;
        private long _totalPeriodicRuns;
        private long _totalReleaseCalls;
        private long _totalReleasedConnections;
        private long _totalTeardownCalls;

        public NetworkMaintenanceCoordinator(int maxTargets = DefaultMaxTargets)
        {
            if (maxTargets <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTargets));

            _periodic = new INetworkPeriodicMaintenanceTarget[maxTargets];
            _release = new INetworkConnectionReleaseTarget[maxTargets];
            _teardown = new INetworkSessionTeardownTarget[maxTargets];
            _lastRunTick = new int[maxTargets];
            _hasRun = new bool[maxTargets];
        }

        public int MaxTargets => _periodic.Length;
        public int PeriodicTargetCount => _periodicCount;
        public int ReleaseTargetCount => _releaseCount;
        public int TeardownTargetCount => _teardownCount;
        public int CurrentTick => _currentTick;
        public long TotalPollCalls => _totalPollCalls;
        public long TotalPeriodicRuns => _totalPeriodicRuns;
        public long TotalReleaseCalls => _totalReleaseCalls;
        public long TotalReleasedConnections => _totalReleasedConnections;
        public long TotalTeardownCalls => _totalTeardownCalls;

        public bool RegisterPeriodic(INetworkPeriodicMaintenanceTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (_periodicCount >= _periodic.Length)
                return false;

            string id = target.MaintenanceId;
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("A periodic maintenance target must have a non-empty id.", nameof(target));
            for (int i = 0; i < _periodicCount; i++)
            {
                if (string.Equals(_periodic[i].MaintenanceId, id, StringComparison.Ordinal))
                    return false;
            }

            _periodic[_periodicCount] = target;
            _lastRunTick[_periodicCount] = 0;
            _hasRun[_periodicCount] = false;
            _periodicCount++;
            return true;
        }

        public bool RegisterConnectionRelease(INetworkConnectionReleaseTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (_releaseCount >= _release.Length)
                return false;

            string id = target.MaintenanceId;
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("A connection-release target must have a non-empty id.", nameof(target));
            for (int i = 0; i < _releaseCount; i++)
            {
                if (string.Equals(_release[i].MaintenanceId, id, StringComparison.Ordinal))
                    return false;
            }

            _release[_releaseCount] = target;
            _releaseCount++;
            return true;
        }

        public bool RegisterSessionTeardown(INetworkSessionTeardownTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (_teardownCount >= _teardown.Length)
                return false;

            string id = target.MaintenanceId;
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("A session-teardown target must have a non-empty id.", nameof(target));
            for (int i = 0; i < _teardownCount; i++)
            {
                if (string.Equals(_teardown[i].MaintenanceId, id, StringComparison.Ordinal))
                    return false;
            }

            _teardown[_teardownCount] = target;
            _teardownCount++;
            return true;
        }

        /// <summary>Broadcasts session shutdown to every registered teardown target.</summary>
        public int TeardownSession()
        {
            _totalTeardownCalls++;
            for (int i = 0; i < _teardownCount; i++)
                _teardown[i].TeardownSession();
            return _teardownCount;
        }

        /// <summary>
        /// Runs every periodic target whose modular interval has elapsed. The
        /// first call always runs each target once, so a freshly registered
        /// target is not skipped until its interval passes.
        /// </summary>
        public int Poll(int serverTick)
        {
            _currentTick = serverTick;
            _totalPollCalls++;
            int ran = 0;

            for (int i = 0; i < _periodicCount; i++)
            {
                INetworkPeriodicMaintenanceTarget target = _periodic[i];
                int interval = target.MaintenanceIntervalTicks;
                if (interval <= 0)
                    continue;

                if (!_hasRun[i]
                    || NetworkTickMath.HasElapsed(serverTick, _lastRunTick[i], interval))
                {
                    target.RunPeriodicMaintenance(serverTick);
                    _lastRunTick[i] = serverTick;
                    _hasRun[i] = true;
                    _totalPeriodicRuns++;
                    ran++;
                }
            }

            return ran;
        }

        /// <summary>
        /// Broadcasts one disconnect to every registered release target.
        /// Returns the number of targets notified.
        /// </summary>
        public int ReleaseConnection(int connectionId)
        {
            if (connectionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(connectionId));

            _totalReleaseCalls++;
            _totalReleasedConnections++;
            for (int i = 0; i < _releaseCount; i++)
                _release[i].ReleaseConnection(connectionId);

            return _releaseCount;
        }

        /// <summary>
        /// Pure coverage self-check. Writes deterministic issues into the caller
        /// supplied span and returns the count; the fingerprint (FNV-1a64 via
        /// Hash.Core) lets CI compare two diagnostics runs without string diffs.
        /// The span must be large enough for all possible issues, otherwise the
        /// call fails explicitly instead of silently truncating.
        /// </summary>
        public int Evaluate(Span<NetworkMaintenanceIssue> issues, out ulong fingerprint)
        {
            int requiredCapacity = NetworkMaintenanceRequirements.RequiredCount
                                   + _periodicCount + _releaseCount + _teardownCount + 2;
            if (issues.Length < requiredCapacity)
            {
                throw new ArgumentException(
                    "Issue span is too small for the current maintenance coverage.",
                    nameof(issues));
            }

            int count = 0;
            ulong hash = Fnv1a64.OffsetBasis;

            IReadOnlyList<string> requiredPeriodic = NetworkMaintenanceRequirements.RequiredPeriodicIds;
            for (int r = 0; r < requiredPeriodic.Count; r++)
            {
                if (!ContainsPeriodic(requiredPeriodic[r]))
                {
                    Add(issues, ref count, ref hash,
                        NetworkMaintenanceSeverity.Error, "MAINT-001",
                        "Required periodic maintenance target is not registered.",
                        "Register a periodic target with this id so its state is reclaimed every tick.",
                        requiredPeriodic[r], 0);
                }
            }

            IReadOnlyList<string> requiredRelease = NetworkMaintenanceRequirements.RequiredReleaseIds;
            for (int r = 0; r < requiredRelease.Count; r++)
            {
                if (!ContainsRelease(requiredRelease[r]))
                {
                    Add(issues, ref count, ref hash,
                        NetworkMaintenanceSeverity.Error, "MAINT-002",
                        "Required connection-release target is not registered.",
                        "Register a connection-release target with this id and call ReleaseConnection on disconnect.",
                        requiredRelease[r], 0);
                }
            }

            IReadOnlyList<string> requiredTeardown = NetworkMaintenanceRequirements.RequiredTeardownIds;
            for (int r = 0; r < requiredTeardown.Count; r++)
            {
                if (!ContainsSessionTeardown(requiredTeardown[r]))
                {
                    Add(issues, ref count, ref hash,
                        NetworkMaintenanceSeverity.Error, "MAINT-006",
                        "Required session-teardown target is not registered.",
                        "Register a teardown target with this id and call TeardownSession at session shutdown.",
                        requiredTeardown[r], 0);
                }
            }

            for (int i = 0; i < _periodicCount; i++)
            {
                INetworkPeriodicMaintenanceTarget target = _periodic[i];
                if (target.MaintenanceIntervalTicks <= 0)
                {
                    Add(issues, ref count, ref hash,
                        NetworkMaintenanceSeverity.Warning, "MAINT-003",
                        "Periodic target has a non-positive interval.",
                        "Set MaintenanceIntervalTicks to at least 1.",
                        target.MaintenanceId, target.MaintenanceIntervalTicks);
                    continue;
                }

                if (_hasRun[i])
                {
                    int elapsed = NetworkTickMath.Delta(_currentTick, _lastRunTick[i]);
                    int limit = target.MaintenanceIntervalTicks * OverdueIntervalMultiplier;
                    if (elapsed > limit)
                    {
                        Add(issues, ref count, ref hash,
                            NetworkMaintenanceSeverity.Warning, "MAINT-004",
                            "Periodic target has not run within its expected interval.",
                            "Call Poll(serverTick) once per network tick.",
                            target.MaintenanceId, elapsed);
                    }
                }
            }

            for (int i = 0; i < _releaseCount; i++)
            {
                if (_release[i].MaintenanceId.Length == 0)
                {
                    Add(issues, ref count, ref hash,
                        NetworkMaintenanceSeverity.Warning, "MAINT-005",
                        "A connection-release target has an empty id.",
                        "Assign a stable id so Editor diagnostics can identify it.",
                        string.Empty, 0);
                }
            }

            fingerprint = hash == 0UL ? Fnv1a64.OffsetBasis : hash;
            return count;
        }

        public bool ContainsPeriodic(string id)
        {
            for (int i = 0; i < _periodicCount; i++)
            {
                if (string.Equals(_periodic[i].MaintenanceId, id, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public bool ContainsRelease(string id)
        {
            for (int i = 0; i < _releaseCount; i++)
            {
                if (string.Equals(_release[i].MaintenanceId, id, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public bool ContainsSessionTeardown(string id)
        {
            for (int i = 0; i < _teardownCount; i++)
            {
                if (string.Equals(_teardown[i].MaintenanceId, id, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public void Reset()
        {
            for (int i = 0; i < _periodicCount; i++)
            {
                _periodic[i] = null;
                _lastRunTick[i] = 0;
                _hasRun[i] = false;
            }
            for (int i = 0; i < _releaseCount; i++)
                _release[i] = null;
            for (int i = 0; i < _teardownCount; i++)
                _teardown[i] = null;

            _periodicCount = 0;
            _releaseCount = 0;
            _teardownCount = 0;
            _currentTick = 0;
            _totalPollCalls = 0;
            _totalPeriodicRuns = 0;
            _totalReleaseCalls = 0;
            _totalReleasedConnections = 0;
            _totalTeardownCalls = 0;
        }

        private static void Add(
            Span<NetworkMaintenanceIssue> issues,
            ref int count,
            ref ulong hash,
            NetworkMaintenanceSeverity severity,
            string code,
            string message,
            string action,
            string targetId,
            int detail)
        {
            issues[count] = new NetworkMaintenanceIssue(
                severity, code, message, action, targetId, detail);
            count++;

            unchecked
            {
                FoldText(ref hash, code);
                FoldText(ref hash, message);
                FoldText(ref hash, targetId);
                hash ^= (byte)severity;
                hash *= Fnv1a64.Prime;
                for (int b = 0; b < 4; b++)
                {
                    hash ^= (byte)(detail >> (b * 8));
                    hash *= Fnv1a64.Prime;
                }
            }
        }

        private static void FoldText(ref ulong hash, string text)
        {
            if (text == null)
                return;
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    hash ^= (byte)c;
                    hash *= Fnv1a64.Prime;
                    hash ^= (byte)(c >> 8);
                    hash *= Fnv1a64.Prime;
                }
            }
        }
    }
}
