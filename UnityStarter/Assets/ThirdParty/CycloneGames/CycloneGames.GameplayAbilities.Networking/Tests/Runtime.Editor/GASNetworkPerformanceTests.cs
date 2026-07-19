using System;

using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Runtime.Editor
{
    public sealed class GASNetworkPerformanceTests
    {
        [Test, Performance]
        public void AbilityCommandCodec_SixteenActorTargets_Benchmark()
        {
            var targets = new GASNetworkEntityId[16];
            var decodedTargets = new GASNetworkEntityId[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = new GASNetworkEntityId((ulong)(i + 100));
            }

            var command = new GASAbilityCommand(
                7u,
                11u,
                new GASNetworkEntityId(13UL),
                new GASNetworkGrantId(17UL),
                GASAbilityCommandKind.ConfirmTarget,
                GASTargetDataKind.ActorList,
                (byte)targets.Length);
            var payload = new byte[GASNetworkWireCodec.MaxAbilityCommandPayloadBytes];
            int checksum = 0;

            Assert.That(ExerciseAbilityCommandCodec(in command, targets, decodedTargets, payload), Is.GreaterThan(0));

            Measure.Method(() =>
                {
                    checksum ^= ExerciseAbilityCommandCodec(in command, targets, decodedTargets, payload);
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(1_000)
                .GC()
                .Run();

            Assert.That(decodedTargets[targets.Length - 1], Is.EqualTo(targets[targets.Length - 1]));
            GC.KeepAlive(checksum);
        }

        [Test, Performance]
        public void StateBatchCodec_ThirtyTwoAttributes_Benchmark()
        {
            const int AttributeCount = 32;
            var attributes = new GASAttributeStateRecord[AttributeCount];
            var decodedAttributes = new GASAttributeStateRecord[AttributeCount];
            for (int i = 0; i < attributes.Length; i++)
            {
                attributes[i] = new GASAttributeStateRecord(
                    GASStateRecordOperation.Upsert,
                    new GASNetworkContentId((ulong)(i + 1)),
                    i * 100L,
                    i * 100L + 50L);
            }

            var header = new GASStateBatchChunk(
                23u,
                29u,
                new GASNetworkEntityId(31UL),
                GASStateBatchKind.Snapshot,
                0UL,
                37UL,
                41u,
                0,
                1,
                0,
                AttributeCount,
                0,
                0,
                0,
                0,
                43UL);
            var payload = new byte[GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes];
            int checksum = 0;

            Assert.That(ExerciseStateBatchCodec(in header, attributes, decodedAttributes, payload), Is.GreaterThan(0));

            Measure.Method(() =>
                {
                    checksum ^= ExerciseStateBatchCodec(in header, attributes, decodedAttributes, payload);
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(1_000)
                .GC()
                .Run();

            Assert.That(decodedAttributes[AttributeCount - 1].CurrentValueRaw,
                Is.EqualTo(attributes[AttributeCount - 1].CurrentValueRaw));
            GC.KeepAlive(checksum);
        }

        private static int ExerciseAbilityCommandCodec(
            in GASAbilityCommand command,
            GASNetworkEntityId[] targets,
            GASNetworkEntityId[] decodedTargets,
            byte[] payload)
        {
            GASNetworkWireCodecResult writeResult = GASNetworkWireCodec.TryWriteAbilityCommand(
                in command,
                targets,
                payload,
                out int bytesWritten);
            if (writeResult != GASNetworkWireCodecResult.Success)
            {
                throw new InvalidOperationException($"Ability command encoding failed: {writeResult}.");
            }

            GASNetworkWireCodecResult readResult = GASNetworkWireCodec.TryReadAbilityCommand(
                new ReadOnlySpan<byte>(payload, 0, bytesWritten),
                decodedTargets,
                out GASAbilityCommand decoded,
                out int targetCount);
            if (readResult != GASNetworkWireCodecResult.Success)
            {
                throw new InvalidOperationException($"Ability command decoding failed: {readResult}.");
            }

            return checked(bytesWritten + targetCount + (int)decoded.CommandSequence);
        }

        private static int ExerciseStateBatchCodec(
            in GASStateBatchChunk header,
            GASAttributeStateRecord[] attributes,
            GASAttributeStateRecord[] decodedAttributes,
            byte[] payload)
        {
            GASNetworkWireCodecResult writeResult = GASNetworkWireCodec.TryWriteStateBatchChunk(
                in header,
                ReadOnlySpan<GASAbilityStateRecord>.Empty,
                attributes,
                ReadOnlySpan<GASEffectStateRecord>.Empty,
                ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                payload,
                out int bytesWritten);
            if (writeResult != GASNetworkWireCodecResult.Success)
            {
                throw new InvalidOperationException($"State batch encoding failed: {writeResult}.");
            }

            GASNetworkWireCodecResult readResult = GASNetworkWireCodec.TryReadStateBatchChunk(
                new ReadOnlySpan<byte>(payload, 0, bytesWritten),
                Span<GASAbilityStateRecord>.Empty,
                decodedAttributes,
                Span<GASEffectStateRecord>.Empty,
                Span<GASEffectTagStateRecord>.Empty,
                Span<GASEffectMagnitudeStateRecord>.Empty,
                Span<GASLooseTagStateRecord>.Empty,
                out GASStateBatchChunk decoded);
            if (readResult != GASNetworkWireCodecResult.Success)
            {
                throw new InvalidOperationException($"State batch decoding failed: {readResult}.");
            }

            return checked(bytesWritten + decoded.AttributeCount + (int)decoded.BatchSequence);
        }
    }
}
