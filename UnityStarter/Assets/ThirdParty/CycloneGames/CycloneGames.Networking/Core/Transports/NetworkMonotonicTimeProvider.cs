using System;
using System.Diagnostics;

namespace CycloneGames.Networking.Transports
{
    /// <summary>
    /// Explicit monotonic clock contract for transport decorators. Wall-clock sources
    /// (DateTime.UtcNow) can step backwards through NTP or user clock changes, which
    /// turns elapsed-time comparisons negative and suppresses expiry/pruning forever.
    /// Implementations must be monotonic within a process lifetime and report a fixed
    /// tick frequency. No shared mutable state is required; each owner may construct
    /// its own provider.
    /// </summary>
    public interface INetworkMonotonicTimeProvider
    {
        long TicksPerSecond { get; }
        long GetMonotonicTicks();
    }

    /// <summary>
    /// Default Stopwatch-backed provider. Stateless, allocation-free after
    /// construction, and safe to construct per transport or per composition root.
    /// </summary>
    public sealed class NetworkStopwatchTimeProvider : INetworkMonotonicTimeProvider
    {
        public long TicksPerSecond => Stopwatch.Frequency;

        public long GetMonotonicTicks() => Stopwatch.GetTimestamp();
    }
}
