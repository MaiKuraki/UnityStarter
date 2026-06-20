using System;
using CycloneGames.Networking.Replication;
using CycloneGames.Networking.Security;

namespace CycloneGames.Networking
{
    public readonly struct NetworkProtocolFuzzValidationOptions
    {
        public readonly int Iterations;
        public readonly int MaxPayloadBytes;
        public readonly int ValidFrameInterval;
        public readonly uint Seed;

        public NetworkProtocolFuzzValidationOptions(
            int iterations,
            int maxPayloadBytes,
            int validFrameInterval = 8,
            uint seed = 1u)
        {
            if (iterations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations));
            }

            if (maxPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
            }

            if (validFrameInterval <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(validFrameInterval));
            }

            Iterations = iterations;
            MaxPayloadBytes = maxPayloadBytes;
            ValidFrameInterval = validFrameInterval;
            Seed = seed == 0u ? 1u : seed;
        }
    }

    public readonly struct NetworkProtocolFuzzValidationResult
    {
        public readonly int ValidFrames;
        public readonly int MalformedFrames;
        public readonly int RejectedMalformedFrames;
        public readonly int FailureCount;

        public NetworkProtocolFuzzValidationResult(
            int validFrames,
            int malformedFrames,
            int rejectedMalformedFrames,
            int failureCount)
        {
            ValidFrames = validFrames;
            MalformedFrames = malformedFrames;
            RejectedMalformedFrames = rejectedMalformedFrames;
            FailureCount = failureCount;
        }

        public double RejectedMalformedRatio
        {
            get
            {
                return MalformedFrames > 0
                    ? (double)RejectedMalformedFrames / MalformedFrames
                    : 1d;
            }
        }
    }

    public static class NetworkProtocolFuzzValidationProbe
    {
        public static NetworkValidationEvidence Run(in NetworkProtocolFuzzValidationOptions options)
        {
            NetworkProtocolFuzzValidationResult result = Execute(options);
            bool passed = result.ValidFrames > 0
                          && result.FailureCount == 0
                          && result.RejectedMalformedFrames == result.MalformedFrames;

            return new NetworkValidationEvidence(
                NetworkValidationIds.ProtocolFuzz,
                passed,
                iterations: options.Iterations,
                rejectedRatio: result.RejectedMalformedRatio,
                failureCount: result.FailureCount,
                description: "Deterministic Cyclone wire-frame fuzz validation.");
        }

        public static NetworkProtocolFuzzValidationResult Execute(in NetworkProtocolFuzzValidationOptions options)
        {
            var random = new ValidationRandom(options.Seed);
            int validFrames = 0;
            int malformedFrames = 0;
            int rejectedMalformedFrames = 0;
            int failureCount = 0;

            for (int i = 0; i < options.Iterations; i++)
            {
                bool shouldBeValid = i % options.ValidFrameInterval == 0;
                if (shouldBeValid)
                {
                    if (!ValidateGeneratedFrame(ref random, options.MaxPayloadBytes))
                    {
                        failureCount++;
                    }

                    validFrames++;
                    continue;
                }

                if (ValidateMalformedFrameRejected(ref random, options.MaxPayloadBytes))
                {
                    rejectedMalformedFrames++;
                }
                else
                {
                    failureCount++;
                }

                malformedFrames++;
            }

            return new NetworkProtocolFuzzValidationResult(
                validFrames,
                malformedFrames,
                rejectedMalformedFrames,
                failureCount);
        }

        private static bool ValidateGeneratedFrame(ref ValidationRandom random, int maxPayloadBytes)
        {
            int payloadLength = 1 + random.NextInt(maxPayloadBytes);
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength + payloadLength];
            for (int i = 0; i < payloadLength; i++)
            {
                frame[NetworkWireProtocol.HeaderLength + i] = (byte)random.NextInt(256);
            }

            ushort messageId = (ushort)(1 + random.NextInt(NetworkConstants.MaxMessageId - 1));
            var channel = (NetworkChannel)(random.NextInt(4));
            uint sequence = (uint)(1 + random.NextInt(int.MaxValue));
            var flags = random.NextInt(2) == 0
                ? NetworkMessageFlags.None
                : NetworkMessageFlags.Reliable;
            uint checksum = NetworkFrameCodec.ComputeChecksum(
                messageId,
                channel,
                flags,
                sequence,
                new ReadOnlySpan<byte>(frame, NetworkWireProtocol.HeaderLength, payloadLength));
            var header = new NetworkEnvelopeHeader(
                messageId,
                channel,
                payloadLength,
                sequence,
                checksum,
                flags);
            NetworkFrameCodec.WriteHeader(frame, 0, header);

            if (NetworkFrameCodec.TryReadPayload(
                    new ArraySegment<byte>(frame),
                    out NetworkEnvelopeHeader readHeader,
                    out ArraySegment<byte> payload) != NetworkFrameResult.Valid)
            {
                return false;
            }

            return payload.Array != null
                   && payload.Count == payloadLength
                   && NetworkFrameCodec.ValidateChecksum(
                       readHeader,
                       new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count)) == NetworkFrameResult.Valid;
        }

        private static bool ValidateMalformedFrameRejected(ref ValidationRandom random, int maxPayloadBytes)
        {
            int length = random.NextInt(NetworkWireProtocol.HeaderLength + maxPayloadBytes + 1);
            byte[] frame = new byte[length];
            for (int i = 0; i < frame.Length; i++)
            {
                frame[i] = (byte)random.NextInt(256);
            }

            NetworkFrameResult result = NetworkFrameCodec.TryReadPayload(
                new ArraySegment<byte>(frame),
                out NetworkEnvelopeHeader header,
                out ArraySegment<byte> payload);
            if (result != NetworkFrameResult.Valid)
            {
                return true;
            }

            if (payload.Array == null)
            {
                return true;
            }

            return NetworkFrameCodec.ValidateChecksum(
                header,
                new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count)) != NetworkFrameResult.Valid;
        }
    }

    public readonly struct NetworkReplicationLoadValidationOptions
    {
        public readonly NetworkReplicationLoadSimulationOptions SimulationOptions;
        public readonly int SameAreaConnectionCount;
        public readonly long MaximumAverageBytesPerPlan;

        public NetworkReplicationLoadValidationOptions(
            in NetworkReplicationLoadSimulationOptions simulationOptions,
            int sameAreaConnectionCount = 0,
            long maximumAverageBytesPerPlan = -1L)
        {
            if (sameAreaConnectionCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sameAreaConnectionCount));
            }

            if (maximumAverageBytesPerPlan < -1L)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumAverageBytesPerPlan));
            }

            SimulationOptions = simulationOptions;
            SameAreaConnectionCount = sameAreaConnectionCount;
            MaximumAverageBytesPerPlan = maximumAverageBytesPerPlan;
        }
    }

    public static class NetworkReplicationLoadValidationProbe
    {
        public static NetworkValidationEvidence Run(in NetworkReplicationLoadValidationOptions options)
        {
            var simulator = new NetworkReplicationLoadSimulator();
            NetworkReplicationLoadSimulationResult result = simulator.Run(options.SimulationOptions);
            bool passed = result.TotalPlans > 0;
            if (options.MaximumAverageBytesPerPlan >= 0L
                && result.AverageEstimatedBytesPerPlan > options.MaximumAverageBytesPerPlan)
            {
                passed = false;
            }

            return new NetworkValidationEvidence(
                NetworkValidationIds.LoadSimulation,
                passed,
                connectionCount: options.SimulationOptions.ConnectionCount,
                sameAreaConnectionCount: options.SameAreaConnectionCount > 0
                    ? options.SameAreaConnectionCount
                    : options.SimulationOptions.ConnectionCount,
                iterations: result.TotalPlans,
                failureCount: passed ? 0 : 1,
                description: "Deterministic replication planner load validation.");
        }
    }

    public static class NetworkValidationEvidenceFactory
    {
        public static NetworkValidationEvidence Passed(
            NetworkValidationId id,
            int connectionCount = 0,
            int sameAreaConnectionCount = 0,
            int iterations = 0,
            double durationSeconds = 0d,
            long allocatedBytesPerTick = -1L,
            double rejectedRatio = -1d,
            string description = "")
        {
            return new NetworkValidationEvidence(
                id,
                passed: true,
                connectionCount,
                sameAreaConnectionCount,
                iterations,
                durationSeconds,
                allocatedBytesPerTick,
                rejectedRatio,
                failureCount: 0,
                description);
        }

        public static NetworkValidationEvidence Failed(
            NetworkValidationId id,
            int connectionCount = 0,
            int sameAreaConnectionCount = 0,
            int iterations = 0,
            double durationSeconds = 0d,
            long allocatedBytesPerTick = -1L,
            double rejectedRatio = -1d,
            int failureCount = 1,
            string description = "")
        {
            return new NetworkValidationEvidence(
                id,
                passed: false,
                connectionCount,
                sameAreaConnectionCount,
                iterations,
                durationSeconds,
                allocatedBytesPerTick,
                rejectedRatio,
                failureCount,
                description);
        }
    }

    internal struct ValidationRandom
    {
        private uint _state;

        public ValidationRandom(uint seed)
        {
            _state = seed == 0u ? 1u : seed;
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                return 0;
            }

            return (int)(NextUInt() % (uint)maxExclusive);
        }

        private uint NextUInt()
        {
            _state = unchecked(_state * 1664525u + 1013904223u);
            return _state;
        }
    }
}
