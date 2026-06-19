using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    public readonly struct NetworkFaultCase
    {
        public readonly NetworkFaultId Id;
        public readonly double DurationSeconds;
        public readonly double Intensity;
        public readonly string Description;

        public NetworkFaultCase(
            NetworkFaultId id,
            double durationSeconds,
            double intensity = 1d,
            string description = "")
        {
            if (durationSeconds < 0d || double.IsNaN(durationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            }

            if (intensity < 0d || double.IsNaN(intensity))
            {
                throw new ArgumentOutOfRangeException(nameof(intensity));
            }

            Id = id;
            DurationSeconds = durationSeconds;
            Intensity = intensity;
            Description = description ?? string.Empty;
        }
    }

    public readonly struct NetworkFaultRequirement
    {
        public readonly NetworkFaultId Id;
        public readonly NetworkReadinessSeverity Severity;
        public readonly double MinimumDurationSeconds;
        public readonly double MinimumIntensity;
        public readonly string Description;

        public NetworkFaultRequirement(
            NetworkFaultId id,
            NetworkReadinessSeverity severity = NetworkReadinessSeverity.Required,
            double minimumDurationSeconds = 0d,
            double minimumIntensity = 0d,
            string description = "")
        {
            if (minimumDurationSeconds < 0d || double.IsNaN(minimumDurationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(minimumDurationSeconds));
            }

            if (minimumIntensity < 0d || double.IsNaN(minimumIntensity))
            {
                throw new ArgumentOutOfRangeException(nameof(minimumIntensity));
            }

            Id = id;
            Severity = severity;
            MinimumDurationSeconds = minimumDurationSeconds;
            MinimumIntensity = minimumIntensity;
            Description = description ?? string.Empty;
        }
    }

    public sealed class NetworkFailureInjectionPlan
    {
        private readonly NetworkFaultCase[] _faults;
        private readonly Dictionary<string, string> _metadata;

        internal NetworkFailureInjectionPlan(NetworkFailureInjectionPlanBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            PlanId = string.IsNullOrEmpty(builder.PlanId) ? "default" : builder.PlanId;
            DisplayName = string.IsNullOrEmpty(builder.DisplayName) ? PlanId : builder.DisplayName;
            _faults = builder.Faults.ToArray();
            _metadata = new Dictionary<string, string>(builder.Metadata, StringComparer.Ordinal);
        }

        public string PlanId { get; }
        public string DisplayName { get; }

        public IReadOnlyList<NetworkFaultCase> Faults
        {
            get
            {
                return _faults;
            }
        }

        public IReadOnlyDictionary<string, string> Metadata
        {
            get
            {
                return _metadata;
            }
        }

        public bool Covers(NetworkFaultRequirement requirement)
        {
            for (int i = 0; i < _faults.Length; i++)
            {
                NetworkFaultCase fault = _faults[i];
                if (fault.Id.Equals(requirement.Id)
                    && fault.DurationSeconds >= requirement.MinimumDurationSeconds
                    && fault.Intensity >= requirement.MinimumIntensity)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class NetworkFailureInjectionPlanBuilder
    {
        internal readonly List<NetworkFaultCase> Faults = new List<NetworkFaultCase>(8);
        internal readonly Dictionary<string, string> Metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        public string PlanId { get; set; } = "default";
        public string DisplayName { get; set; } = "Default";

        public NetworkFailureInjectionPlanBuilder AddFault(
            NetworkFaultId id,
            double durationSeconds,
            double intensity = 1d,
            string description = "")
        {
            Faults.Add(new NetworkFaultCase(id, durationSeconds, intensity, description));
            return this;
        }

        public NetworkFailureInjectionPlanBuilder SetMetadata(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Fault plan metadata key must not be null or empty.", nameof(key));
            }

            Metadata[key] = value ?? string.Empty;
            return this;
        }

        public NetworkFailureInjectionPlan Build()
        {
            return new NetworkFailureInjectionPlan(this);
        }
    }
}
