using System;
using CycloneGames.Hash.Core;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Binds a validation evidence record to the runner, build, platform, start
    /// time, seed and source digest that produced it. RunDigest is a deterministic
    /// FNV-1a64 over the evidence counters and provenance fields.
    ///
    /// This is a provenance check, not a signature. Products that require tamper
    /// resistance must add a keyed MAC.
    /// </summary>
    public readonly struct NetworkValidationEvidenceOrigin : IEquatable<NetworkValidationEvidenceOrigin>
    {
        public readonly string BuildId;
        public readonly string Platform;
        public readonly string RunnerId;
        public readonly long StartedAtUtcTicks;
        public readonly ulong Seed;
        public readonly ulong SourceDigest;
        public readonly ulong RunDigest;

        public NetworkValidationEvidenceOrigin(
            string buildId,
            string platform,
            string runnerId,
            long startedAtUtcTicks,
            ulong seed,
            ulong sourceDigest,
            ulong runDigest = 0UL)
        {
            BuildId = buildId ?? string.Empty;
            Platform = platform ?? string.Empty;
            RunnerId = runnerId ?? string.Empty;
            StartedAtUtcTicks = startedAtUtcTicks;
            Seed = seed;
            SourceDigest = sourceDigest;
            RunDigest = runDigest;
        }

        // A default(struct) instance has null string fields; use null-safe checks
        // so an untrusted/empty origin is simply incomplete, never an exception.
        public bool IsComplete =>
            !string.IsNullOrEmpty(BuildId)
            && !string.IsNullOrEmpty(Platform)
            && !string.IsNullOrEmpty(RunnerId)
            && StartedAtUtcTicks > 0L
            && SourceDigest != 0UL
            && RunDigest != 0UL;

        /// <summary>
        /// Completes the digest for an origin whose RunDigest is not yet assigned.
        /// </summary>
        public NetworkValidationEvidenceOrigin WithRunDigest(ulong runDigest)
        {
            return new NetworkValidationEvidenceOrigin(
                BuildId, Platform, RunnerId, StartedAtUtcTicks, Seed, SourceDigest, runDigest);
        }

        public bool Matches(in NetworkValidationEvidence evidence)
        {
            return IsComplete && RunDigest == ComputeDigest(evidence);
        }

        /// <summary>
        /// Deterministic FNV-1a64 over every evidence counter plus the provenance
        /// fields. RunDigest itself is intentionally excluded to avoid a fixpoint.
        /// </summary>
        internal static ulong ComputeDigest(in NetworkValidationEvidence evidence)
        {
            ulong hash = Fnv1a64.OffsetBasis;
            hash = Fold(hash, evidence.Passed ? 1UL : 0UL);
            hash = Fold(hash, (ulong)(uint)evidence.ConnectionCount);
            hash = Fold(hash, (ulong)(uint)evidence.SameAreaConnectionCount);
            hash = Fold(hash, (ulong)(uint)evidence.Iterations);
            hash = Fold(hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(evidence.DurationSeconds)));
            hash = Fold(hash, unchecked((ulong)evidence.AllocatedBytesPerTick));
            hash = Fold(hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(evidence.RejectedRatio)));
            hash = Fold(hash, (ulong)(uint)evidence.FailureCount);

            hash = FoldText(hash, evidence.Id.Value);
            hash = FoldText(hash, evidence.Origin.BuildId);
            hash = FoldText(hash, evidence.Origin.Platform);
            hash = FoldText(hash, evidence.Origin.RunnerId);
            hash = Fold(hash, unchecked((ulong)evidence.Origin.StartedAtUtcTicks));
            hash = Fold(hash, evidence.Origin.Seed);
            hash = Fold(hash, evidence.Origin.SourceDigest);
            return hash == 0UL ? Fnv1a64.OffsetBasis : hash;
        }

        private static ulong Fold(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= Fnv1a64.Prime;
                }
                return hash;
            }
        }

        private static ulong FoldText(ulong hash, string text)
        {
            if (text == null)
                return hash;
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
                return hash;
            }
        }

        public bool Equals(NetworkValidationEvidenceOrigin other)
        {
            return string.Equals(BuildId, other.BuildId, StringComparison.Ordinal)
                   && string.Equals(Platform, other.Platform, StringComparison.Ordinal)
                   && string.Equals(RunnerId, other.RunnerId, StringComparison.Ordinal)
                   && StartedAtUtcTicks == other.StartedAtUtcTicks
                   && Seed == other.Seed
                   && SourceDigest == other.SourceDigest
                   && RunDigest == other.RunDigest;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkValidationEvidenceOrigin other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = StringComparer.Ordinal.GetHashCode(BuildId ?? string.Empty);
                h = (h * 397) ^ StringComparer.Ordinal.GetHashCode(Platform ?? string.Empty);
                h = (h * 397) ^ StringComparer.Ordinal.GetHashCode(RunnerId ?? string.Empty);
                h = (h * 397) ^ StartedAtUtcTicks.GetHashCode();
                h = (h * 397) ^ Seed.GetHashCode();
                h = (h * 397) ^ SourceDigest.GetHashCode();
                h = (h * 397) ^ RunDigest.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>
    /// Explicit execution context supplied by the runner that produced the evidence.
    /// Core never infers these values from the environment (no reflection, no
    /// Assembly/SystemInfo probing), so a product must state them.
    /// </summary>
    public readonly struct NetworkValidationExecutionContext
    {
        public readonly string BuildId;
        public readonly string Platform;
        public readonly string RunnerId;
        public readonly long StartedAtUtcTicks;
        public readonly ulong Seed;
        public readonly ulong SourceDigest;

        public NetworkValidationExecutionContext(
            string buildId,
            string platform,
            string runnerId,
            long startedAtUtcTicks,
            ulong seed,
            ulong sourceDigest)
        {
            if (string.IsNullOrEmpty(buildId)) throw new ArgumentException("Build id is required.", nameof(buildId));
            if (string.IsNullOrEmpty(platform)) throw new ArgumentException("Platform is required.", nameof(platform));
            if (string.IsNullOrEmpty(runnerId)) throw new ArgumentException("Runner id is required.", nameof(runnerId));
            if (startedAtUtcTicks <= 0L) throw new ArgumentOutOfRangeException(nameof(startedAtUtcTicks));
            if (sourceDigest == 0UL) throw new ArgumentOutOfRangeException(nameof(sourceDigest));

            BuildId = buildId;
            Platform = platform;
            RunnerId = runnerId;
            StartedAtUtcTicks = startedAtUtcTicks;
            Seed = seed;
            SourceDigest = sourceDigest;
        }
    }

    /// <summary>
    /// Executes a real validation probe and produces trusted evidence bound to the
    /// supplied execution context. This is the built-in path whose counters are
    /// guaranteed to come from an actual run rather than from caller input.
    /// </summary>
    public static class NetworkValidationProbeRunner
    {
        public static NetworkValidationEvidence ExecuteFuzz(
            in NetworkValidationExecutionContext context,
            in NetworkProtocolFuzzValidationOptions options)
        {
            NetworkProtocolFuzzValidationResult result =
                NetworkProtocolFuzzValidationProbe.Execute(options);

            bool passed = result.ValidFrames > 0
                          && result.FailureCount == 0
                          && result.RejectedMalformedFrames == result.MalformedFrames;

            return NetworkValidationEvidenceFactory.FromExecution(
                NetworkValidationIds.ProtocolFuzz,
                context,
                passed,
                iterations: options.Iterations,
                rejectedRatio: result.RejectedMalformedRatio,
                failureCount: result.FailureCount,
                description: "Deterministic Cyclone wire-frame fuzz validation (trusted runner).");
        }
    }
}
