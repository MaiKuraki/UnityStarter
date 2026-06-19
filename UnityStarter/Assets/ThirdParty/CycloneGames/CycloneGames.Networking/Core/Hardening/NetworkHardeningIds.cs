using System;

namespace CycloneGames.Networking
{
    public readonly struct NetworkHardeningScenarioId : IEquatable<NetworkHardeningScenarioId>
    {
        public readonly string Value;

        public NetworkHardeningScenarioId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Hardening scenario id must not be null or empty.", nameof(value));
            }

            Value = value;
        }

        public bool Equals(NetworkHardeningScenarioId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkHardeningScenarioId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }

    public readonly struct NetworkHardeningRequirementId : IEquatable<NetworkHardeningRequirementId>
    {
        public readonly string Value;

        public NetworkHardeningRequirementId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Hardening requirement id must not be null or empty.", nameof(value));
            }

            Value = value;
        }

        public bool Equals(NetworkHardeningRequirementId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkHardeningRequirementId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }

    public readonly struct NetworkFaultId : IEquatable<NetworkFaultId>
    {
        public readonly string Value;

        public NetworkFaultId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Network fault id must not be null or empty.", nameof(value));
            }

            Value = value;
        }

        public bool Equals(NetworkFaultId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkFaultId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }

    public enum NetworkReadinessSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Required = 2,
        Critical = 3
    }

    public static class NetworkHardeningRequirementIds
    {
        public static readonly NetworkHardeningRequirementId RuntimeProfile = new NetworkHardeningRequirementId("runtime.profile");
        public static readonly NetworkHardeningRequirementId ConnectionCapacity = new NetworkHardeningRequirementId("capacity.connections");
        public static readonly NetworkHardeningRequirementId PayloadBudget = new NetworkHardeningRequirementId("capacity.payload");
        public static readonly NetworkHardeningRequirementId TickBudget = new NetworkHardeningRequirementId("simulation.tick_budget");
        public static readonly NetworkHardeningRequirementId TimeoutCoherence = new NetworkHardeningRequirementId("session.timeout_coherence");
        public static readonly NetworkHardeningRequirementId SessionSearch = new NetworkHardeningRequirementId("session.search");
        public static readonly NetworkHardeningRequirementId Reconnection = new NetworkHardeningRequirementId("session.reconnection");
        public static readonly NetworkHardeningRequirementId HostMigration = new NetworkHardeningRequirementId("session.host_migration");
        public static readonly NetworkHardeningRequirementId NodeCapabilities = new NetworkHardeningRequirementId("node.capabilities");
        public static readonly NetworkHardeningRequirementId ProtocolManifest = new NetworkHardeningRequirementId("protocol.manifest");
        public static readonly NetworkHardeningRequirementId FaultCoverage = new NetworkHardeningRequirementId("fault.coverage");
    }

    public static class NetworkFaultIds
    {
        public static readonly NetworkFaultId Latency = new NetworkFaultId("net.latency");
        public static readonly NetworkFaultId Jitter = new NetworkFaultId("net.jitter");
        public static readonly NetworkFaultId PacketLoss = new NetworkFaultId("net.packet_loss");
        public static readonly NetworkFaultId PacketDuplication = new NetworkFaultId("net.packet_duplication");
        public static readonly NetworkFaultId PacketReorder = new NetworkFaultId("net.packet_reorder");
        public static readonly NetworkFaultId BandwidthCap = new NetworkFaultId("net.bandwidth_cap");
        public static readonly NetworkFaultId ClientDisconnect = new NetworkFaultId("session.client_disconnect");
        public static readonly NetworkFaultId HostDisconnect = new NetworkFaultId("session.host_disconnect");
        public static readonly NetworkFaultId ReconnectStorm = new NetworkFaultId("session.reconnect_storm");
        public static readonly NetworkFaultId BackendUnavailable = new NetworkFaultId("backend.unavailable");
        public static readonly NetworkFaultId ProtocolMismatch = new NetworkFaultId("protocol.mismatch");
        public static readonly NetworkFaultId ClockDrift = new NetworkFaultId("simulation.clock_drift");
        public static readonly NetworkFaultId MobileSuspend = new NetworkFaultId("platform.mobile_suspend");
        public static readonly NetworkFaultId WebGLThrottle = new NetworkFaultId("platform.webgl_throttle");
    }
}
