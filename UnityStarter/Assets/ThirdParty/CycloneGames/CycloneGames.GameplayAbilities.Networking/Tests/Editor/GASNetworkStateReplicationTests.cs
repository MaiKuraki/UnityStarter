using System;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASNetworkStateReplicationTests
    {
        private static readonly GASNetworkEntityId Entity = new GASNetworkEntityId(100UL);

        [Test]
        public void Snapshot_IsInvisibleUntilEveryChunkIsPreparedAndCommitted()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateBuffer state = CreateMinimalState(content, 1UL, 1u, 900L);
            var receiver = CreateReceiver(content, DefaultCapacity());

            var first = new GASStateBatchChunk(
                7u, 1u, Entity, GASStateBatchKind.Snapshot, 0UL, state.StateVersion,
                state.LastProcessedCommandSequence, 0, 2, 1, 0, 0, 0, 0, 0, state.StateChecksum);
            GASStateReceiveResult firstResult = receiver.ReceiveChunk(
                in first,
                state.Abilities,
                ReadOnlySpan<GASAttributeStateRecord>.Empty,
                ReadOnlySpan<GASEffectStateRecord>.Empty,
                ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                ReadOnlySpan<GASLooseTagStateRecord>.Empty);

            Assert.That(firstResult, Is.EqualTo(GASStateReceiveResult.Partial));
            Assert.That(receiver.CurrentState.StateVersion, Is.Zero);
            Assert.That(receiver.PreparedState, Is.Null);
            GASStateBatchChunk unrelatedInvalid = default;
            Assert.That(ReceiveEmpty(receiver, in unrelatedInvalid), Is.EqualTo(GASStateReceiveResult.WrongStream));

            var unrelatedFuture = new GASStateBatchChunk(
                7u, 2u, Entity, GASStateBatchKind.Snapshot, 0UL, 2UL, 1u,
                0, 1, 0, 0, 0, 0, 0, 0, 1UL);
            Assert.That(ReceiveEmpty(receiver, in unrelatedFuture), Is.EqualTo(GASStateReceiveResult.SequenceGap));

            var second = new GASStateBatchChunk(
                7u, 1u, Entity, GASStateBatchKind.Snapshot, 0UL, state.StateVersion,
                state.LastProcessedCommandSequence, 1, 2, 0, 1, 0, 0, 0, 0, state.StateChecksum);
            GASStateReceiveResult secondResult = receiver.ReceiveChunk(
                in second,
                ReadOnlySpan<GASAbilityStateRecord>.Empty,
                state.Attributes,
                ReadOnlySpan<GASEffectStateRecord>.Empty,
                ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                ReadOnlySpan<GASLooseTagStateRecord>.Empty);

            Assert.That(secondResult, Is.EqualTo(GASStateReceiveResult.Prepared));
            Assert.That(receiver.CurrentState.StateVersion, Is.Zero);
            Assert.That(receiver.PreparedState, Is.Not.Null);
            Assert.That(receiver.PreparedState.StateChecksum, Is.EqualTo(state.StateChecksum));

            GASStateAcknowledgement acknowledgement = receiver.CommitPrepared();
            Assert.That(acknowledgement.IsValid, Is.True);
            Assert.That(acknowledgement.BatchSequence, Is.EqualTo(1u));
            Assert.That(acknowledgement.AppliedStateVersion, Is.EqualTo(state.StateVersion));
            Assert.That(acknowledgement.StateChecksum, Is.EqualTo(state.StateChecksum));
            Assert.That(receiver.CurrentState.StateChecksum, Is.EqualTo(state.StateChecksum));
            Assert.That(receiver.ExpectedBatchSequence, Is.EqualTo(2u));
            Assert.That(receiver.HasPreparedState, Is.False);

            Assert.That(receiver.ReceiveChunk(
                    in first,
                    state.Abilities,
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty),
                Is.EqualTo(GASStateReceiveResult.Duplicate));
        }

        [Test]
        public void RejectPrepared_PreservesVisibleStateAndAllowsTheBatchToBeRetried()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateBuffer initial = CreateMinimalState(content, 1UL, 1u, 900L);
            GASNetworkStateBuffer replacement = CreateMinimalState(content, 2UL, 2u, 750L);
            var receiver = CreateReceiver(content, DefaultCapacity());
            CommitSnapshot(receiver, initial, 1u);

            GASStateBatchChunk replacementHeader = CreateSnapshotHeader(replacement, 2u);
            Assert.That(ReceiveFullState(receiver, in replacementHeader, replacement),
                Is.EqualTo(GASStateReceiveResult.Prepared));
            Assert.That(receiver.CurrentState.StateChecksum, Is.EqualTo(initial.StateChecksum));

            receiver.RejectPrepared();
            Assert.That(receiver.CurrentState.StateChecksum, Is.EqualTo(initial.StateChecksum));
            Assert.That(receiver.ExpectedBatchSequence, Is.EqualTo(2u));
            Assert.That(receiver.PreparedState, Is.Null);

            Assert.That(ReceiveFullState(receiver, in replacementHeader, replacement),
                Is.EqualTo(GASStateReceiveResult.Prepared));
            GASStateAcknowledgement acknowledgement = receiver.CommitPrepared();
            Assert.That(acknowledgement.BatchSequence, Is.EqualTo(2u));
            Assert.That(receiver.CurrentState.StateChecksum, Is.EqualTo(replacement.StateChecksum));
        }

        [Test]
        public void LocalStateLostResync_AllowsOneSameVersionCanonicalSnapshot()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateBuffer state = CreateMinimalState(content, 1UL, 1u, 900L);
            var receiver = CreateReceiver(content, DefaultCapacity());
            CommitSnapshot(receiver, state, 1u);

            GASStateBatchChunk repeated = CreateSnapshotHeader(state, 2u);
            Assert.That(
                ReceiveFullState(receiver, in repeated, state),
                Is.EqualTo(GASStateReceiveResult.BaselineMismatch));

            GASResyncRequest request = receiver.CreateResyncRequest(GASResyncReason.LocalStateLost);
            Assert.That(request.ObservedStateVersion, Is.EqualTo(state.StateVersion));
            Assert.That(request.ExpectedBatchSequence, Is.EqualTo(2u));
            Assert.That(
                ReceiveFullState(receiver, in repeated, state),
                Is.EqualTo(GASStateReceiveResult.Prepared));

            GASStateAcknowledgement acknowledgement = receiver.CommitPrepared();
            Assert.That(acknowledgement.AppliedStateVersion, Is.EqualTo(state.StateVersion));
            Assert.That(receiver.ExpectedBatchSequence, Is.EqualTo(3u));
            GASStateBatchChunk repeatedAgain = CreateSnapshotHeader(state, 3u);
            Assert.That(
                ReceiveFullState(receiver, in repeatedAgain, state),
                Is.EqualTo(GASStateReceiveResult.BaselineMismatch));
        }

        [Test]
        public void Delta_BuildAndApply_ReproducesCanonicalStateAndChecksum()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateCapacity capacity = DefaultCapacity();
            GASNetworkStateBuffer baseline = CreateRichState(content, 1UL, 1u, currentVariant: false);
            GASNetworkStateBuffer current = CreateRichState(content, 2UL, 2u, currentVariant: true);
            var delta = new GASNetworkStateDeltaBuffer(capacity);

            Assert.That(delta.TryBuild(baseline, current), Is.True);
            Assert.That(delta.IsEmpty, Is.False);
            Assert.That(delta.AbilityCount, Is.EqualTo(2), "One grant removal and one grant addition are required.");
            Assert.That(delta.EffectCount, Is.EqualTo(1));
            Assert.That(delta.Effects[0].SourceStreamEpoch, Is.EqualTo(18u));
            Assert.That(delta.EffectTagCount, Is.EqualTo(1), "The removed effect tag must be explicit while its parent remains.");
            Assert.That(delta.EffectMagnitudes.Length, Is.EqualTo(2));

            var receiver = CreateReceiver(content, capacity);
            CommitSnapshot(receiver, baseline, 1u);
            var header = new GASStateBatchChunk(
                7u, 2u, Entity, GASStateBatchKind.Delta,
                baseline.StateVersion, current.StateVersion, current.LastProcessedCommandSequence,
                0, 1,
                checked((ushort)delta.AbilityCount),
                checked((ushort)delta.AttributeCount),
                checked((ushort)delta.EffectCount),
                checked((ushort)delta.EffectTagCount),
                checked((ushort)delta.EffectMagnitudeCount),
                checked((ushort)delta.LooseTagCount),
                current.StateChecksum);

            GASStateReceiveResult result = receiver.ReceiveChunk(
                in header,
                delta.Abilities,
                delta.Attributes,
                delta.Effects,
                delta.EffectTags,
                delta.EffectMagnitudes,
                delta.LooseTags);

            Assert.That(result, Is.EqualTo(GASStateReceiveResult.Prepared));
            Assert.That(receiver.CurrentState.StateChecksum, Is.EqualTo(baseline.StateChecksum));
            Assert.That(receiver.PreparedState.StateChecksum, Is.EqualTo(current.StateChecksum));
            GASStateAcknowledgement acknowledgement = receiver.CommitPrepared();
            Assert.That(acknowledgement.StateChecksum, Is.EqualTo(current.StateChecksum));
            Assert.That(receiver.CurrentState.AbilityCount, Is.EqualTo(current.AbilityCount));
            Assert.That(receiver.CurrentState.EffectTagCount, Is.EqualTo(current.EffectTagCount));
            Assert.That(receiver.CurrentState.EffectMagnitudeCount, Is.EqualTo(current.EffectMagnitudeCount));
            Assert.That(receiver.CurrentState.GetAttribute(0).CurrentValueRaw,
                Is.EqualTo(current.GetAttribute(0).CurrentValueRaw));
        }

        [Test]
        public void ChecksumMismatch_DiscardsCandidateAndProducesBoundedResyncRequest()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateBuffer state = CreateMinimalState(content, 1UL, 1u, 900L);
            var receiver = CreateReceiver(content, DefaultCapacity());
            GASStateBatchChunk correct = CreateSnapshotHeader(state, 1u);
            var wrong = new GASStateBatchChunk(
                correct.StreamEpoch, correct.BatchSequence, correct.Entity, correct.Kind,
                correct.BaseStateVersion, correct.StateVersion, correct.LastProcessedCommandSequence,
                correct.ChunkIndex, correct.ChunkCount,
                correct.AbilityCount, correct.AttributeCount, correct.EffectCount,
                correct.EffectTagCount, correct.EffectMagnitudeCount, correct.LooseTagCount,
                correct.StateChecksum + 1UL);

            Assert.That(ReceiveFullState(receiver, in wrong, state),
                Is.EqualTo(GASStateReceiveResult.ChecksumMismatch));
            Assert.That(receiver.CurrentState.StateVersion, Is.Zero);
            Assert.That(receiver.PreparedState, Is.Null);
            Assert.That(receiver.ExpectedBatchSequence, Is.EqualTo(1u));
            Assert.That(GASNetworkStateReceiver.TryGetResyncReason(
                    GASStateReceiveResult.ChecksumMismatch, out GASResyncReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(GASResyncReason.ChecksumMismatch));

            GASResyncRequest request = receiver.CreateResyncRequest(reason);
            Assert.That(request.IsValid, Is.True);
            Assert.That(request.RequestSequence, Is.EqualTo(1u));
            Assert.That(request.ObservedStateVersion, Is.Zero);
            Assert.That(request.ExpectedBatchSequence, Is.EqualTo(1u));
        }

        [Test]
        public void SequenceGapAndBaselineMismatch_DoNotMutateCommittedState()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateBuffer initial = CreateMinimalState(content, 1UL, 1u, 900L);
            var receiver = CreateReceiver(content, DefaultCapacity());

            GASStateBatchChunk future = CreateSnapshotHeader(initial, 2u);
            Assert.That(ReceiveFullState(receiver, in future, initial), Is.EqualTo(GASStateReceiveResult.SequenceGap));

            var ability = new GASAbilityStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkGrantId(10UL),
                content.Ability,
                default,
                1,
                GASAbilityStateFlags.None);
            var noBaseline = new GASStateBatchChunk(
                7u, 1u, Entity, GASStateBatchKind.Delta, 1UL, 2UL, 1u,
                0, 1, 1, 0, 0, 0, 0, 0, 1UL);
            Assert.That(receiver.ReceiveChunk(
                    in noBaseline,
                    new[] { ability },
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty),
                Is.EqualTo(GASStateReceiveResult.BaselineMismatch));

            CommitSnapshot(receiver, initial, 1u);
            var wrongBaseline = new GASStateBatchChunk(
                7u, 2u, Entity, GASStateBatchKind.Delta, 99UL, 100UL, 2u,
                0, 1, 1, 0, 0, 0, 0, 0, 1UL);
            Assert.That(receiver.ReceiveChunk(
                    in wrongBaseline,
                    new[] { ability },
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty),
                Is.EqualTo(GASStateReceiveResult.BaselineMismatch));
            Assert.That(receiver.CurrentState.StateChecksum, Is.EqualTo(initial.StateChecksum));

            GASNetworkStateBuffer staleSnapshot = CreateMinimalState(content, 1UL, 2u, 700L);
            GASStateBatchChunk staleHeader = CreateSnapshotHeader(staleSnapshot, 2u);
            Assert.That(ReceiveFullState(receiver, in staleHeader, staleSnapshot),
                Is.EqualTo(GASStateReceiveResult.BaselineMismatch));
            Assert.That(receiver.CurrentState.StateChecksum, Is.EqualTo(initial.StateChecksum));
        }

        [Test]
        public void Receiver_RejectsContentIdsFromTheWrongSemanticKind()
        {
            CatalogFixture content = CreateCatalog();
            var receiver = CreateReceiver(content, DefaultCapacity());
            var wrongAbility = new GASAbilityStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkGrantId(1UL),
                content.Effect,
                default,
                1,
                GASAbilityStateFlags.None);
            var abilityHeader = new GASStateBatchChunk(
                7u, 1u, Entity, GASStateBatchKind.Snapshot, 0UL, 1UL, 0u,
                0, 1, 1, 0, 0, 0, 0, 0, 1UL);

            Assert.That(receiver.ReceiveChunk(
                    in abilityHeader,
                    new[] { wrongAbility },
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty),
                Is.EqualTo(GASStateReceiveResult.InvalidRecord));

            var effect = new GASEffectStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkEffectId(2UL),
                content.Effect,
                default,
                0u,
                default,
                1,
                1,
                10L,
                5L,
                0L,
                0u,
                GASEffectStateFlags.None);
            var wrongNameMagnitude = new GASEffectMagnitudeStateRecord(
                GASStateRecordOperation.Upsert,
                effect.Effect,
                GASNetworkMagnitudeKey.FromName(content.Ability),
                1L);
            var magnitudeHeader = new GASStateBatchChunk(
                7u, 1u, Entity, GASStateBatchKind.Snapshot, 0UL, 1UL, 0u,
                0, 1, 0, 0, 1, 0, 1, 0, 1UL);
            Assert.That(receiver.ReceiveChunk(
                    in magnitudeHeader,
                    ReadOnlySpan<GASAbilityStateRecord>.Empty,
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    new[] { effect },
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    new[] { wrongNameMagnitude },
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty),
                Is.EqualTo(GASStateReceiveResult.InvalidRecord));
        }

        [Test]
        public void Receiver_RejectsCapacityOverflowWithoutPublishingPartialState()
        {
            CatalogFixture content = CreateCatalog();
            var capacity = new GASNetworkStateCapacity(1, 0, 0, 0, 0, 0);
            var receiver = CreateReceiver(content, capacity);
            GASAbilityStateRecord[] abilities =
            {
                new GASAbilityStateRecord(GASStateRecordOperation.Upsert, new GASNetworkGrantId(1UL), content.Ability, default, 1, default),
                new GASAbilityStateRecord(GASStateRecordOperation.Upsert, new GASNetworkGrantId(2UL), content.Ability, default, 1, default)
            };
            var header = new GASStateBatchChunk(
                7u, 1u, Entity, GASStateBatchKind.Snapshot, 0UL, 1UL, 0u,
                0, 1, 2, 0, 0, 0, 0, 0, 1UL);

            Assert.That(receiver.ReceiveChunk(
                    in header,
                    abilities,
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty),
                Is.EqualTo(GASStateReceiveResult.CapacityExceeded));
            Assert.That(receiver.CurrentState.StateVersion, Is.Zero);
            Assert.That(receiver.PreparedState, Is.Null);
            Assert.That(GASNetworkStateReceiver.TryGetResyncReason(
                    GASStateReceiveResult.CapacityExceeded, out GASResyncReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(GASResyncReason.ApplyFailure));
        }

        [Test]
        public void EffectChildren_RequireAnExistingParentEffect()
        {
            CatalogFixture content = CreateCatalog();
            var receiver = CreateReceiver(content, DefaultCapacity());
            var orphan = new GASEffectTagStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkEffectId(999UL),
                new GASNetworkTagId(500UL),
                GASEffectTagKind.Granted);
            var header = new GASStateBatchChunk(
                7u, 1u, Entity, GASStateBatchKind.Snapshot, 0UL, 1UL, 0u,
                0, 1, 0, 0, 0, 1, 0, 0, 1UL);

            Assert.That(receiver.ReceiveChunk(
                    in header,
                    ReadOnlySpan<GASAbilityStateRecord>.Empty,
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    new[] { orphan },
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty),
                Is.EqualTo(GASStateReceiveResult.InvalidRecord));
            Assert.That(receiver.PreparedState, Is.Null);

            var orphanGrantedAbility = new GASAbilityStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkGrantId(1UL),
                content.Ability,
                new GASNetworkEffectId(999UL),
                1,
                GASAbilityStateFlags.None);
            var abilityHeader = new GASStateBatchChunk(
                7u, 1u, Entity, GASStateBatchKind.Snapshot, 0UL, 1UL, 0u,
                0, 1, 1, 0, 0, 0, 0, 0, 1UL);
            Assert.That(receiver.ReceiveChunk(
                    in abilityHeader,
                    new[] { orphanGrantedAbility },
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty),
                Is.EqualTo(GASStateReceiveResult.InvalidRecord));
        }

        [Test]
        public void CanonicalOrdering_MakesChecksumIndependentOfInsertionOrder()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateCapacity capacity = DefaultCapacity();
            var first = new GASNetworkStateBuffer(capacity);
            var second = new GASNetworkStateBuffer(capacity);
            first.BeginWrite(Entity, 3UL, 2u);
            second.BeginWrite(Entity, 3UL, 2u);

            var low = new GASAbilityStateRecord(
                GASStateRecordOperation.Upsert, new GASNetworkGrantId(1UL), content.Ability, default, 1, default);
            var high = new GASAbilityStateRecord(
                GASStateRecordOperation.Upsert, new GASNetworkGrantId(9UL), content.Ability, default, 2, GASAbilityStateFlags.Active);
            Assert.That(first.TrySetAbility(in high), Is.True);
            Assert.That(first.TrySetAbility(in low), Is.True);
            Assert.That(second.TrySetAbility(in low), Is.True);
            Assert.That(second.TrySetAbility(in high), Is.True);

            Assert.That(first.TryCompleteWrite(), Is.True);
            Assert.That(second.TryCompleteWrite(), Is.True);
            Assert.That(first.GetAbility(0).Grant, Is.EqualTo(low.Grant));
            Assert.That(second.GetAbility(0).Grant, Is.EqualTo(low.Grant));
            Assert.That(first.StateChecksum, Is.EqualTo(second.StateChecksum));
        }

        [Test]
        public void Delta_RejectsRegressedCommandSequenceAndClearsPreviousOutput()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateCapacity capacity = DefaultCapacity();
            GASNetworkStateBuffer baseline = CreateMinimalState(content, 1UL, 5u, 900L);
            GASNetworkStateBuffer validCurrent = CreateMinimalState(content, 2UL, 6u, 800L);
            GASNetworkStateBuffer regressed = CreateMinimalState(content, 3UL, 4u, 700L);
            var delta = new GASNetworkStateDeltaBuffer(capacity);

            Assert.That(delta.TryBuild(baseline, validCurrent), Is.True);
            Assert.That(delta.IsEmpty, Is.False);
            Assert.That(delta.TryBuild(baseline, regressed), Is.False);
            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void Receiver_RequiresEpochRotationAfterMaximumBatchSequence()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateBuffer state = CreateMinimalState(content, 1UL, 1u, 900L);
            var receiver = new GASNetworkStateReceiver(
                7u,
                Entity,
                content.Catalog,
                DefaultCapacity(),
                GameplayAbilitiesNetworkProtocol.MaxSequence);
            GASStateBatchChunk header = CreateSnapshotHeader(
                state,
                GameplayAbilitiesNetworkProtocol.MaxSequence);

            Assert.That(ReceiveFullState(receiver, in header, state), Is.EqualTo(GASStateReceiveResult.Prepared));
            receiver.CommitPrepared();
            Assert.That(receiver.ExpectedBatchSequence, Is.Zero);
            Assert.That(ReceiveFullState(receiver, in header, state), Is.EqualTo(GASStateReceiveResult.SequenceExhausted));
            Assert.Throws<InvalidOperationException>(() => receiver.CreateResyncRequest(GASResyncReason.SequenceGap));

            receiver.ResetStream(8u);
            Assert.That(receiver.ExpectedBatchSequence, Is.EqualTo(1u));
        }

        [Test]
        public void WarmStateBuildDeltaAndReceivePath_AllocatesNoManagedMemoryOnEditorMono()
        {
            CatalogFixture content = CreateCatalog();
            GASNetworkStateCapacity capacity = DefaultCapacity();
            GASNetworkStateBuffer baseline = CreateRichState(content, 1UL, 1u, currentVariant: false);
            GASNetworkStateBuffer current = CreateRichState(content, 2UL, 2u, currentVariant: true);
            var delta = new GASNetworkStateDeltaBuffer(capacity);
            var receiver = CreateReceiver(content, capacity);
            uint streamEpoch = receiver.StreamEpoch;
            int checksum = 0;

            for (int i = 0; i < 64; i++)
                checksum += ExerciseWarmStatePath(baseline, current, delta, receiver, ref streamEpoch);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4_096; i++)
                checksum += ExerciseWarmStatePath(baseline, current, delta, receiver, ref streamEpoch);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(checksum, Is.GreaterThan(0));
            Assert.That(allocated, Is.Zero);
        }

        private static GASNetworkStateReceiver CreateReceiver(
            CatalogFixture content,
            GASNetworkStateCapacity capacity)
        {
            return new GASNetworkStateReceiver(7u, Entity, content.Catalog, capacity);
        }

        private static GASNetworkStateCapacity DefaultCapacity()
        {
            return new GASNetworkStateCapacity(8, 8, 8, 16, 16, 16);
        }

        private static CatalogFixture CreateCatalog()
        {
            var builder = new GASNetworkContentCatalogBuilder();
            GASNetworkContentId ability = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "Ability.Primary",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("ability.primary:1"));
            GASNetworkContentId effect = builder.Add(
                GASNetworkContentKind.EffectDefinition,
                "Effect.Primary",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("effect.primary:1"));
            GASNetworkContentId attribute = builder.Add(
                GASNetworkContentKind.Attribute,
                "Attribute.Health",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("attribute.health:1"));
            GASNetworkContentId setByCallerName = builder.Add(
                GASNetworkContentKind.SetByCallerName,
                "SetByCaller.Damage",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("setbycaller.damage:1"));
            return new CatalogFixture(builder.Build(), ability, effect, attribute, setByCallerName);
        }

        private static GASNetworkStateBuffer CreateMinimalState(
            CatalogFixture content,
            ulong stateVersion,
            uint lastCommand,
            long currentValueRaw)
        {
            var state = new GASNetworkStateBuffer(DefaultCapacity());
            state.BeginWrite(Entity, stateVersion, lastCommand);
            var ability = new GASAbilityStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkGrantId(10UL),
                content.Ability,
                default,
                1,
                GASAbilityStateFlags.None);
            var attribute = new GASAttributeStateRecord(
                GASStateRecordOperation.Upsert,
                content.Attribute,
                1_000L,
                currentValueRaw);
            Assert.That(state.TrySetAbility(in ability), Is.True);
            Assert.That(state.TrySetAttribute(in attribute), Is.True);
            Assert.That(state.TryCompleteWrite(), Is.True);
            return state;
        }

        private static GASNetworkStateBuffer CreateRichState(
            CatalogFixture content,
            ulong stateVersion,
            uint lastCommand,
            bool currentVariant)
        {
            var state = new GASNetworkStateBuffer(DefaultCapacity());
            state.BeginWrite(Entity, stateVersion, lastCommand);
            var ability = new GASAbilityStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkGrantId(currentVariant ? 12UL : 11UL),
                content.Ability,
                new GASNetworkEffectId(20UL),
                currentVariant ? 2 : 1,
                currentVariant ? GASAbilityStateFlags.Active : GASAbilityStateFlags.None);
            var attribute = new GASAttributeStateRecord(
                GASStateRecordOperation.Upsert,
                content.Attribute,
                1_000L,
                currentVariant ? 700L : 900L);
            var effect = new GASEffectStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkEffectId(20UL),
                content.Effect,
                new GASNetworkEntityId(200UL),
                currentVariant ? 18u : 17u,
                new GASNetworkGrantId(currentVariant ? 12UL : 11UL),
                1,
                currentVariant ? 2 : 1,
                1_000L,
                currentVariant ? 600L : 800L,
                100L,
                lastCommand,
                GASEffectStateFlags.None);
            var effectTag = new GASEffectTagStateRecord(
                GASStateRecordOperation.Upsert,
                effect.Effect,
                new GASNetworkTagId(30UL),
                GASEffectTagKind.Granted);
            var nameMagnitude = new GASEffectMagnitudeStateRecord(
                GASStateRecordOperation.Upsert,
                effect.Effect,
                GASNetworkMagnitudeKey.FromName(content.SetByCallerName),
                currentVariant ? 300L : 200L);
            var tagMagnitude = new GASEffectMagnitudeStateRecord(
                GASStateRecordOperation.Upsert,
                effect.Effect,
                GASNetworkMagnitudeKey.FromTag(new GASNetworkTagId(31UL)),
                400L);
            var looseTag = new GASLooseTagStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkTagId(40UL),
                currentVariant ? 2 : 1);

            Assert.That(state.TrySetAbility(in ability), Is.True);
            Assert.That(state.TrySetAttribute(in attribute), Is.True);
            Assert.That(state.TrySetEffect(in effect), Is.True);
            if (!currentVariant)
                Assert.That(state.TrySetEffectTag(in effectTag), Is.True);
            Assert.That(state.TrySetEffectMagnitude(in nameMagnitude), Is.True);
            if (currentVariant)
                Assert.That(state.TrySetEffectMagnitude(in tagMagnitude), Is.True);
            Assert.That(state.TrySetLooseTag(in looseTag), Is.True);
            Assert.That(state.TryCompleteWrite(), Is.True);
            return state;
        }

        private static GASStateBatchChunk CreateSnapshotHeader(GASNetworkStateBuffer state, uint batchSequence)
        {
            return CreateSnapshotHeader(state, batchSequence, 7u);
        }

        private static GASStateBatchChunk CreateSnapshotHeader(
            GASNetworkStateBuffer state,
            uint batchSequence,
            uint streamEpoch)
        {
            return new GASStateBatchChunk(
                streamEpoch,
                batchSequence,
                state.Entity,
                GASStateBatchKind.Snapshot,
                0UL,
                state.StateVersion,
                state.LastProcessedCommandSequence,
                0,
                1,
                checked((ushort)state.AbilityCount),
                checked((ushort)state.AttributeCount),
                checked((ushort)state.EffectCount),
                checked((ushort)state.EffectTagCount),
                checked((ushort)state.EffectMagnitudeCount),
                checked((ushort)state.LooseTagCount),
                state.StateChecksum);
        }

        private static int ExerciseWarmStatePath(
            GASNetworkStateBuffer baseline,
            GASNetworkStateBuffer current,
            GASNetworkStateDeltaBuffer delta,
            GASNetworkStateReceiver receiver,
            ref uint streamEpoch)
        {
            if (!current.TryCompleteWrite() || !delta.TryBuild(baseline, current))
                return -1;

            streamEpoch++;
            receiver.ResetStream(streamEpoch);
            GASStateBatchChunk header = CreateSnapshotHeader(current, 1u, streamEpoch);
            if (ReceiveFullState(receiver, in header, current) != GASStateReceiveResult.Prepared)
                return -2;

            GASStateAcknowledgement acknowledgement = receiver.CommitPrepared();
            return checked(delta.TotalRecordCount + (int)acknowledgement.BatchSequence);
        }

        private static GASStateReceiveResult ReceiveFullState(
            GASNetworkStateReceiver receiver,
            in GASStateBatchChunk header,
            GASNetworkStateBuffer state)
        {
            return receiver.ReceiveChunk(
                in header,
                state.Abilities,
                state.Attributes,
                state.Effects,
                state.EffectTags,
                state.EffectMagnitudes,
                state.LooseTags);
        }

        private static void CommitSnapshot(
            GASNetworkStateReceiver receiver,
            GASNetworkStateBuffer state,
            uint batchSequence)
        {
            GASStateBatchChunk header = CreateSnapshotHeader(state, batchSequence);
            Assert.That(ReceiveFullState(receiver, in header, state), Is.EqualTo(GASStateReceiveResult.Prepared));
            GASStateAcknowledgement acknowledgement = receiver.CommitPrepared();
            Assert.That(acknowledgement.StateChecksum, Is.EqualTo(state.StateChecksum));
        }

        private static GASStateReceiveResult ReceiveEmpty(
            GASNetworkStateReceiver receiver,
            in GASStateBatchChunk header)
        {
            return receiver.ReceiveChunk(
                in header,
                ReadOnlySpan<GASAbilityStateRecord>.Empty,
                ReadOnlySpan<GASAttributeStateRecord>.Empty,
                ReadOnlySpan<GASEffectStateRecord>.Empty,
                ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                ReadOnlySpan<GASLooseTagStateRecord>.Empty);
        }

        private readonly struct CatalogFixture
        {
            public CatalogFixture(
                GASNetworkContentCatalog catalog,
                GASNetworkContentId ability,
                GASNetworkContentId effect,
                GASNetworkContentId attribute,
                GASNetworkContentId setByCallerName)
            {
                Catalog = catalog;
                Ability = ability;
                Effect = effect;
                Attribute = attribute;
                SetByCallerName = setByCallerName;
            }

            public GASNetworkContentCatalog Catalog { get; }
            public GASNetworkContentId Ability { get; }
            public GASNetworkContentId Effect { get; }
            public GASNetworkContentId Attribute { get; }
            public GASNetworkContentId SetByCallerName { get; }
        }
    }
}
