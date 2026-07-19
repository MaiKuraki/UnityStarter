using System;
using CycloneGames.Networking;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GameplayAbilitiesNetworkingProtocolTests
    {
        [Test]
        public void ProtocolManifest_DeclaresCompleteBoundedV1Catalog()
        {
            NetworkProtocolManifest manifest = GameplayAbilitiesNetworkProtocol.CreateProtocolManifest();
            ushort[] expectedIds =
            {
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                GameplayAbilitiesNetworkProtocol.CommandResultMessageId,
                GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId,
                GameplayAbilitiesNetworkProtocol.StateAcknowledgementMessageId,
                GameplayAbilitiesNetworkProtocol.ResyncRequestMessageId,
                GameplayAbilitiesNetworkProtocol.CueExecutedMessageId
            };
            int[] expectedSizes =
            {
                GASNetworkWireCodec.HandshakePayloadBytes,
                GASNetworkWireCodec.MaxAbilityCommandPayloadBytes,
                GASNetworkWireCodec.CommandResultPayloadBytes,
                GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes,
                GASNetworkWireCodec.StateAcknowledgementPayloadBytes,
                GASNetworkWireCodec.ResyncRequestPayloadBytes,
                GASNetworkWireCodec.CueExecutedPayloadBytes
            };

            Assert.That(manifest.CurrentVersion, Is.EqualTo(1));
            Assert.That(manifest.MinimumSupportedVersion, Is.EqualTo(1));
            Assert.That(manifest.MessageRange.Min, Is.EqualTo(10000));
            Assert.That(manifest.MessageRange.Max, Is.EqualTo(10999));
            Assert.That(manifest.Messages.Count, Is.EqualTo(expectedIds.Length));
            Assert.That(GameplayAbilitiesNetworkProtocol.ProtocolFingerprint, Is.EqualTo(manifest.Fingerprint));
            Assert.That(GameplayAbilitiesNetworkProtocol.WireSchemaFingerprint, Is.Not.Zero);

            for (int i = 0; i < expectedIds.Length; i++)
            {
                Assert.That(manifest.Messages[i].MessageId, Is.EqualTo(expectedIds[i]));
                Assert.That(manifest.Messages[i].MaxPayloadSize, Is.EqualTo(expectedSizes[i]));
                Assert.That(manifest.Messages[i].DefaultChannel, Is.EqualTo(NetworkChannel.Reliable));
                Assert.That(GameplayAbilitiesNetworkProtocol.IsGameplayAbilitiesMessageId(expectedIds[i]), Is.True);
            }
        }

        [Test]
        public void ProtocolRegistration_IsIdempotentAndClaimsOnlyItsRange()
        {
            var catalog = new NetworkMessageCatalog();
            GameplayAbilitiesNetworkProtocol.RegisterMessageCatalog(catalog);
            GameplayAbilitiesNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.MessageCount, Is.EqualTo(7));
            Assert.That(catalog.ManifestCount, Is.EqualTo(1));
            Assert.That(catalog.TryGet(GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId, out NetworkMessageDescriptor descriptor), Is.True);
            Assert.That(descriptor.Owner, Is.EqualTo(GameplayAbilitiesNetworkProtocol.MessageOwner));
            Assert.That(catalog.TryGetRegisteredRange(descriptor.MessageId, out NetworkMessageIdRange range), Is.True);
            Assert.That(range.Min, Is.EqualTo(GameplayAbilitiesNetworkProtocol.MessageIdBase));
            Assert.That(range.Max, Is.EqualTo(GameplayAbilitiesNetworkProtocol.MessageIdMax));
        }

        [Test]
        public void ContentCatalog_IsDeterministicAndRejectsDuplicateRegistration()
        {
            ulong abilityRevision = GASNetworkContentCatalogBuilder.ComputeRevisionHash("ability.fireball:v1");
            ulong effectRevision = GASNetworkContentCatalogBuilder.ComputeRevisionHash("effect.burning:v1");

            var leftBuilder = new GASNetworkContentCatalogBuilder();
            GASNetworkContentId abilityId = leftBuilder.Add(
                GASNetworkContentKind.AbilityDefinition, "ability.fireball", abilityRevision);
            GASNetworkContentId effectId = leftBuilder.Add(
                GASNetworkContentKind.EffectDefinition, "effect.burning", effectRevision);
            GASNetworkContentCatalog left = leftBuilder.Build();

            var rightBuilder = new GASNetworkContentCatalogBuilder();
            rightBuilder.Add(GASNetworkContentKind.EffectDefinition, "effect.burning", effectRevision);
            rightBuilder.Add(GASNetworkContentKind.AbilityDefinition, "ability.fireball", abilityRevision);
            GASNetworkContentCatalog right = rightBuilder.Build();

            Assert.That(abilityId.IsValid, Is.True);
            Assert.That(effectId.IsValid, Is.True);
            Assert.That(abilityId, Is.Not.EqualTo(effectId));
            Assert.That(left.ManifestHash, Is.EqualTo(right.ManifestHash));
            Assert.That(left.TryGetEntry(abilityId, out GASNetworkContentEntry entry), Is.True);
            Assert.That(entry.Kind, Is.EqualTo(GASNetworkContentKind.AbilityDefinition));
            Assert.Throws<InvalidOperationException>(() =>
                leftBuilder.Add(GASNetworkContentKind.AbilityDefinition, "ability.fireball", abilityRevision));
        }

        [Test]
        public void Handshake_RoundTripsAndNegotiatesCatalogTagsAndFeatures()
        {
            const ulong contentHash = 0x1122334455667788UL;
            const ulong tagHash = 0x8877665544332211UL;
            GASNetworkHandshake local = GameplayAbilitiesNetworkProtocol.CreateHandshake(contentHash, tagHash);
            Span<byte> bytes = stackalloc byte[GASNetworkWireCodec.HandshakePayloadBytes];

            Assert.That(GASNetworkWireCodec.TryWriteHandshake(in local, bytes, out int written), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            Assert.That(GASNetworkWireCodec.TryReadHandshake(bytes, out GASNetworkHandshake decoded), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(decoded.ContentCatalogHash, Is.EqualTo(contentHash));
            Assert.That(decoded.GameplayTagManifestHash, Is.EqualTo(tagHash));
            Assert.That(GameplayAbilitiesNetworkProtocol.Negotiate(decoded, contentHash, tagHash), Is.EqualTo(GASNetworkHandshakeResult.Compatible));
            Assert.That(GameplayAbilitiesNetworkProtocol.Negotiate(decoded, contentHash + 1UL, tagHash), Is.EqualTo(GASNetworkHandshakeResult.ContentCatalogMismatch));
            Assert.That(GameplayAbilitiesNetworkProtocol.Negotiate(decoded, contentHash, tagHash + 1UL), Is.EqualTo(GASNetworkHandshakeResult.GameplayTagManifestMismatch));
        }

        [Test]
        public void AbilityCommand_NoTarget_MatchesLittleEndianLayoutAndRoundTrips()
        {
            var command = new GASAbilityCommand(
                0x11223344u,
                0x55667788u,
                new GASNetworkEntityId(0x0102030405060708UL),
                new GASNetworkGrantId(0x1112131415161718UL),
                GASAbilityCommandKind.Activate);
            byte[] bytes = new byte[GASNetworkWireCodec.AbilityCommandHeaderBytes];

            Assert.That(GASNetworkWireCodec.TryWriteAbilityCommand(in command, ReadOnlySpan<GASNetworkEntityId>.Empty, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(28));
            Assert.That(bytes[0], Is.EqualTo(1));
            CollectionAssert.AreEqual(new byte[] { 0x44, 0x33, 0x22, 0x11 }, new ArraySegment<byte>(bytes, 1, 4));
            CollectionAssert.AreEqual(new byte[] { 0x88, 0x77, 0x66, 0x55 }, new ArraySegment<byte>(bytes, 5, 4));
            Assert.That(bytes[25], Is.EqualTo((byte)GASAbilityCommandKind.Activate));
            Assert.That(bytes[26], Is.Zero);
            Assert.That(bytes[27], Is.Zero);

            Assert.That(GASNetworkWireCodec.TryReadAbilityCommand(bytes, Span<GASNetworkEntityId>.Empty, out GASAbilityCommand decoded, out int targetCount),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(targetCount, Is.Zero);
            Assert.That(decoded.Entity, Is.EqualTo(command.Entity));
            Assert.That(decoded.Grant, Is.EqualTo(command.Grant));
        }

        [Test]
        public void AbilityCommand_ActorList_IsBoundedUniqueAndCallerOwned()
        {
            GASNetworkEntityId[] targets =
            {
                new GASNetworkEntityId(101UL),
                new GASNetworkEntityId(102UL),
                new GASNetworkEntityId(103UL)
            };
            var command = new GASAbilityCommand(
                7u, 9u, new GASNetworkEntityId(11UL), new GASNetworkGrantId(13UL),
                GASAbilityCommandKind.ConfirmTarget, GASTargetDataKind.ActorList, (byte)targets.Length);
            var bytes = new byte[GASNetworkWireCodec.MaxAbilityCommandPayloadBytes];

            Assert.That(GASNetworkWireCodec.TryWriteAbilityCommand(in command, targets, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            var decodedTargets = new GASNetworkEntityId[targets.Length];
            Assert.That(GASNetworkWireCodec.TryReadAbilityCommand(
                    new ReadOnlySpan<byte>(bytes, 0, written), decodedTargets, out GASAbilityCommand decoded, out int count),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(count, Is.EqualTo(targets.Length));
            Assert.That(decoded.TargetDataKind, Is.EqualTo(GASTargetDataKind.ActorList));
            CollectionAssert.AreEqual(targets, decodedTargets);

            var tooSmall = new GASNetworkEntityId[targets.Length - 1];
            Assert.That(GASNetworkWireCodec.TryReadAbilityCommand(
                    new ReadOnlySpan<byte>(bytes, 0, written), tooSmall, out _, out _),
                Is.EqualTo(GASNetworkWireCodecResult.DestinationRecordCapacityTooSmall));

            GASNetworkEntityId[] duplicates = { targets[0], targets[0] };
            var duplicateCommand = new GASAbilityCommand(
                7u, 10u, new GASNetworkEntityId(11UL), new GASNetworkGrantId(13UL),
                GASAbilityCommandKind.ConfirmTarget, GASTargetDataKind.ActorList, 2);
            Assert.That(GASNetworkWireCodec.TryWriteAbilityCommand(in duplicateCommand, duplicates, bytes, out _),
                Is.EqualTo(GASNetworkWireCodecResult.MalformedMessage));
        }

        [Test]
        public void AbilityCommandFingerprint_IsCanonicalAndOrderSensitive()
        {
            GASNetworkEntityId[] targets =
            {
                new GASNetworkEntityId(101UL),
                new GASNetworkEntityId(102UL)
            };
            var command = new GASAbilityCommand(
                7u,
                9u,
                new GASNetworkEntityId(11UL),
                new GASNetworkGrantId(13UL),
                GASAbilityCommandKind.ConfirmTarget,
                GASTargetDataKind.ActorList,
                (byte)targets.Length);

            ulong fingerprint = GASNetworkWireCodec.ComputeAbilityCommandFingerprint(in command, targets);
            Assert.That(fingerprint, Is.Not.Zero);
            Assert.That(GASNetworkWireCodec.ComputeAbilityCommandFingerprint(in command, targets), Is.EqualTo(fingerprint));

            GASNetworkEntityId[] reversedTargets = { targets[1], targets[0] };
            Assert.That(
                GASNetworkWireCodec.ComputeAbilityCommandFingerprint(in command, reversedTargets),
                Is.Not.EqualTo(fingerprint));

            var changedCommand = new GASAbilityCommand(
                7u,
                9u,
                new GASNetworkEntityId(11UL),
                new GASNetworkGrantId(14UL),
                GASAbilityCommandKind.ConfirmTarget,
                GASTargetDataKind.ActorList,
                (byte)targets.Length);
            Assert.That(
                GASNetworkWireCodec.ComputeAbilityCommandFingerprint(in changedCommand, targets),
                Is.Not.EqualTo(fingerprint));

            Assert.That(
                GASNetworkWireCodec.ComputeAbilityCommandFingerprint(in command, targets.AsSpan(0, 1)),
                Is.Zero);
        }

        [Test]
        public void SequenceFields_AcceptIntMaxAndRejectLargerValues()
        {
            uint max = GameplayAbilitiesNetworkProtocol.MaxSequence;
            uint tooHigh = max + 1u;
            var entity = new GASNetworkEntityId(5UL);
            var grant = new GASNetworkGrantId(7UL);

            var command = new GASAbilityCommand(2u, max, entity, grant, GASAbilityCommandKind.Activate);
            var invalidCommand = new GASAbilityCommand(2u, tooHigh, entity, grant, GASAbilityCommandKind.Activate);
            Assert.That(GASNetworkMessageValidator.ValidateHeader(in command), Is.EqualTo(GASNetworkMessageValidationResult.Valid));
            Assert.That(GASNetworkMessageValidator.ValidateHeader(in invalidCommand), Is.EqualTo(GASNetworkMessageValidationResult.InvalidSequence));

            var batch = new GASStateBatchChunk(
                2u, max, entity, GASStateBatchKind.Snapshot,
                0UL, 1UL, max, 0, 1, 0, 0, 0, 0, 0, 0, 1UL);
            var invalidBatchSequence = new GASStateBatchChunk(
                2u, tooHigh, entity, GASStateBatchKind.Snapshot,
                0UL, 1UL, max, 0, 1, 0, 0, 0, 0, 0, 0, 1UL);
            var invalidLastCommand = new GASStateBatchChunk(
                2u, max, entity, GASStateBatchKind.Snapshot,
                0UL, 1UL, tooHigh, 0, 1, 0, 0, 0, 0, 0, 0, 1UL);
            Assert.That(GASNetworkMessageValidator.Validate(in batch), Is.EqualTo(GASNetworkMessageValidationResult.Valid));
            Assert.That(GASNetworkMessageValidator.Validate(in invalidBatchSequence), Is.EqualTo(GASNetworkMessageValidationResult.InvalidSequence));
            Assert.That(GASNetworkMessageValidator.Validate(in invalidLastCommand), Is.EqualTo(GASNetworkMessageValidationResult.InvalidStateVersion));

            var resync = new GASResyncRequest(2u, max, entity, 1UL, max, 1UL, GASResyncReason.SequenceGap);
            var invalidResync = new GASResyncRequest(2u, max, entity, 1UL, tooHigh, 1UL, GASResyncReason.SequenceGap);
            Assert.That(GASNetworkMessageValidator.Validate(in resync), Is.EqualTo(GASNetworkMessageValidationResult.Valid));
            Assert.That(GASNetworkMessageValidator.Validate(in invalidResync), Is.EqualTo(GASNetworkMessageValidationResult.InvalidSequence));

            var cue = new GASCueExecuted(
                2u, max, entity, new GASNetworkTagId(11UL), default, default, max, 1UL,
                GASCueEvent.Execute, GASCueFlags.None, 1f, default, default);
            var invalidCue = new GASCueExecuted(
                2u, max, entity, new GASNetworkTagId(11UL), default, default, tooHigh, 1UL,
                GASCueEvent.Execute, GASCueFlags.None, 1f, default, default);
            Assert.That(GASNetworkMessageValidator.Validate(in cue), Is.EqualTo(GASNetworkMessageValidationResult.Valid));
            Assert.That(GASNetworkMessageValidator.Validate(in invalidCue), Is.EqualTo(GASNetworkMessageValidationResult.InvalidSequence));

            var effect = new GASEffectStateRecord(
                GASStateRecordOperation.Upsert, new GASNetworkEffectId(17UL), new GASNetworkContentId(19UL),
                default, 0u, default, 1, 1, 0L, 0L, 0L, max, GASEffectStateFlags.None);
            var invalidEffect = new GASEffectStateRecord(
                GASStateRecordOperation.Upsert, new GASNetworkEffectId(17UL), new GASNetworkContentId(19UL),
                default, 0u, default, 1, 1, 0L, 0L, 0L, tooHigh, GASEffectStateFlags.None);
            Assert.That(GASNetworkMessageValidator.Validate(in effect), Is.EqualTo(GASNetworkMessageValidationResult.Valid));
            Assert.That(GASNetworkMessageValidator.Validate(in invalidEffect), Is.EqualTo(GASNetworkMessageValidationResult.InvalidRecord));
        }

        [Test]
        public void LooseTagCount_IsPositiveAndBounded()
        {
            var tag = new GASNetworkTagId(17UL);
            var zero = new GASLooseTagStateRecord(GASStateRecordOperation.Upsert, tag, 0);
            var maximum = new GASLooseTagStateRecord(
                GASStateRecordOperation.Upsert,
                tag,
                GameplayAbilitiesNetworkProtocol.MaxExplicitLooseTagCount);
            var tooLarge = new GASLooseTagStateRecord(
                GASStateRecordOperation.Upsert,
                tag,
                GameplayAbilitiesNetworkProtocol.MaxExplicitLooseTagCount + 1);

            Assert.That(GASNetworkMessageValidator.Validate(in zero), Is.EqualTo(GASNetworkMessageValidationResult.InvalidRecord));
            Assert.That(GASNetworkMessageValidator.Validate(in maximum), Is.EqualTo(GASNetworkMessageValidationResult.Valid));
            Assert.That(GASNetworkMessageValidator.Validate(in tooLarge), Is.EqualTo(GASNetworkMessageValidationResult.InvalidRecord));
        }

        [Test]
        public void EffectSourceGrant_RequiresItsOwningSourceEpoch()
        {
            var sourceEntity = new GASNetworkEntityId(29UL);
            var sourceGrant = new GASNetworkGrantId(31UL);
            var valid = new GASEffectStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkEffectId(17UL),
                new GASNetworkContentId(19UL),
                sourceEntity,
                37u,
                sourceGrant,
                1,
                1,
                0L,
                0L,
                0L,
                0u,
                GASEffectStateFlags.None);
            var missingEpoch = new GASEffectStateRecord(
                GASStateRecordOperation.Upsert,
                valid.Effect,
                valid.Definition,
                sourceEntity,
                0u,
                sourceGrant,
                1,
                1,
                0L,
                0L,
                0L,
                0u,
                GASEffectStateFlags.None);
            var epochWithoutGrant = new GASEffectStateRecord(
                GASStateRecordOperation.Upsert,
                valid.Effect,
                valid.Definition,
                sourceEntity,
                37u,
                default,
                1,
                1,
                0L,
                0L,
                0L,
                0u,
                GASEffectStateFlags.None);

            Assert.That(
                GASNetworkMessageValidator.Validate(in valid),
                Is.EqualTo(GASNetworkMessageValidationResult.Valid));
            Assert.That(
                GASNetworkMessageValidator.Validate(in missingEpoch),
                Is.EqualTo(GASNetworkMessageValidationResult.InvalidRecord));
            Assert.That(
                GASNetworkMessageValidator.Validate(in epochWithoutGrant),
                Is.EqualTo(GASNetworkMessageValidationResult.InvalidRecord));
        }

        [Test]
        public void AbilityCommand_SingleHit_RoundTripsPortableGeometryAndRejectsNaN()
        {
            var hit = new GASNetworkSingleTargetHit(
                new GASNetworkEntityId(55UL),
                new GASNetworkVector3(1.25f, -2.5f, 3.75f),
                new GASNetworkVector3(0f, 1f, 0f),
                7.5f,
                new GASNetworkContentId(77UL),
                GASTargetHitFlags.BlockingHit | GASTargetHitFlags.HasTargetEntity);
            var command = new GASAbilityCommand(
                2u, 3u, new GASNetworkEntityId(5UL), new GASNetworkGrantId(7UL),
                GASAbilityCommandKind.ConfirmTarget, GASTargetDataKind.SingleHit, 1, singleHit: hit);
            Span<byte> bytes = stackalloc byte[GASNetworkWireCodec.AbilityCommandHeaderBytes + GASNetworkWireCodec.SingleTargetHitPayloadBytes];

            Assert.That(GASNetworkWireCodec.TryWriteAbilityCommand(in command, ReadOnlySpan<GASNetworkEntityId>.Empty, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            Assert.That(GASNetworkWireCodec.TryReadAbilityCommand(bytes, Span<GASNetworkEntityId>.Empty, out GASAbilityCommand decoded, out _),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(decoded.SingleHit.TargetEntity, Is.EqualTo(hit.TargetEntity));
            Assert.That(decoded.SingleHit.Point, Is.EqualTo(hit.Point));
            Assert.That(decoded.SingleHit.Distance, Is.EqualTo(hit.Distance));

            var invalidHit = new GASNetworkSingleTargetHit(default, new GASNetworkVector3(float.NaN, 0f, 0f), default, 0f, default, default);
            var invalidCommand = new GASAbilityCommand(
                2u, 4u, new GASNetworkEntityId(5UL), new GASNetworkGrantId(7UL),
                GASAbilityCommandKind.ConfirmTarget, GASTargetDataKind.SingleHit, 1, singleHit: invalidHit);
            Assert.That(GASNetworkWireCodec.TryWriteAbilityCommand(in invalidCommand, ReadOnlySpan<GASNetworkEntityId>.Empty, bytes, out _),
                Is.EqualTo(GASNetworkWireCodecResult.MalformedMessage));
        }

        [Test]
        public void CommandResult_RoundTripsAndRejectsUnknownStatus()
        {
            var result = new GASCommandResult(
                3u, 5u, new GASNetworkEntityId(7UL), new GASNetworkGrantId(9UL),
                GASAbilityCommandKind.Activate, GASCommandStatus.Accepted, 11UL);
            Span<byte> bytes = stackalloc byte[GASNetworkWireCodec.CommandResultPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteCommandResult(in result, bytes, out int written), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            Assert.That(GASNetworkWireCodec.TryReadCommandResult(bytes, out GASCommandResult decoded), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(decoded.Status, Is.EqualTo(GASCommandStatus.Accepted));
            bytes[26] = byte.MaxValue;
            Assert.That(GASNetworkWireCodec.TryReadCommandResult(bytes, out _), Is.EqualTo(GASNetworkWireCodecResult.MalformedMessage));
        }

        [Test]
        public void StateBatch_RoundTripsAllRecordKindsWithExactRawValues()
        {
            var header = new GASStateBatchChunk(
                3u, 5u, new GASNetworkEntityId(7UL), GASStateBatchKind.Delta,
                100UL, 101UL, 9u, 0, 1, 1, 1, 1, 1, 2, 1, 0x1122334455667788UL);
            GASAbilityStateRecord[] abilities =
            {
                new GASAbilityStateRecord(GASStateRecordOperation.Upsert, new GASNetworkGrantId(11UL),
                    new GASNetworkContentId(13UL), new GASNetworkEffectId(19UL), 4, GASAbilityStateFlags.Active)
            };
            GASAttributeStateRecord[] attributes =
            {
                new GASAttributeStateRecord(GASStateRecordOperation.Upsert, new GASNetworkContentId(17UL),
                    long.MinValue + 1L, long.MaxValue)
            };
            GASEffectStateRecord[] effects =
            {
                new GASEffectStateRecord(GASStateRecordOperation.Upsert, new GASNetworkEffectId(19UL),
                    new GASNetworkContentId(23UL), new GASNetworkEntityId(29UL), 37u,
                    new GASNetworkGrantId(31UL),
                    2, 3, 1_000_000L, 750_000L, 250_000L, 9u, GASEffectStateFlags.Predicted)
            };
            GASEffectTagStateRecord[] effectTags =
            {
                new GASEffectTagStateRecord(GASStateRecordOperation.Upsert, new GASNetworkEffectId(19UL),
                    new GASNetworkTagId(37UL), GASEffectTagKind.Granted)
            };
            GASEffectMagnitudeStateRecord[] magnitudes =
            {
                new GASEffectMagnitudeStateRecord(GASStateRecordOperation.Upsert, new GASNetworkEffectId(19UL),
                    GASNetworkMagnitudeKey.FromName(new GASNetworkContentId(41UL)), -9_876_543_210L),
                new GASEffectMagnitudeStateRecord(GASStateRecordOperation.Upsert, new GASNetworkEffectId(19UL),
                    GASNetworkMagnitudeKey.FromTag(new GASNetworkTagId(47UL)), 123_456_789L)
            };
            GASLooseTagStateRecord[] tags =
            {
                new GASLooseTagStateRecord(GASStateRecordOperation.Upsert, new GASNetworkTagId(43UL), 2)
            };
            var bytes = new byte[GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes];

            Assert.That(GASNetworkWireCodec.TryWriteStateBatchChunk(
                    in header, abilities, attributes, effects, effectTags, magnitudes, tags, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(GASNetworkWireCodec.GetStateBatchPayloadBytes(in header)));

            var readAbilities = new GASAbilityStateRecord[1];
            var readAttributes = new GASAttributeStateRecord[1];
            var readEffects = new GASEffectStateRecord[1];
            var readEffectTags = new GASEffectTagStateRecord[1];
            var readMagnitudes = new GASEffectMagnitudeStateRecord[2];
            var readTags = new GASLooseTagStateRecord[1];
            Assert.That(GASNetworkWireCodec.TryReadStateBatchChunk(
                    new ReadOnlySpan<byte>(bytes, 0, written), readAbilities, readAttributes, readEffects,
                    readEffectTags, readMagnitudes, readTags, out GASStateBatchChunk decoded),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(decoded.StateVersion, Is.EqualTo(101UL));
            Assert.That(readAbilities[0].GrantingEffect, Is.EqualTo(new GASNetworkEffectId(19UL)));
            Assert.That(readAttributes[0].BaseValueRaw, Is.EqualTo(long.MinValue + 1L));
            Assert.That(readAttributes[0].CurrentValueRaw, Is.EqualTo(long.MaxValue));
            Assert.That(readEffects[0].DurationRaw, Is.EqualTo(1_000_000L));
            Assert.That(readEffects[0].RemainingRaw, Is.EqualTo(750_000L));
            Assert.That(readEffects[0].SourceStreamEpoch, Is.EqualTo(37u));
            Assert.That(readMagnitudes[0].ValueRaw, Is.EqualTo(-9_876_543_210L));
            Assert.That(readMagnitudes[0].Key.Kind, Is.EqualTo(GASEffectMagnitudeKeyKind.Name));
            Assert.That(readMagnitudes[0].Key.Name, Is.EqualTo(new GASNetworkContentId(41UL)));
            Assert.That(readMagnitudes[1].ValueRaw, Is.EqualTo(123_456_789L));
            Assert.That(readMagnitudes[1].Key.Kind, Is.EqualTo(GASEffectMagnitudeKeyKind.GameplayTag));
            Assert.That(readMagnitudes[1].Key.Tag, Is.EqualTo(new GASNetworkTagId(47UL)));
            Assert.That(readEffectTags[0].Tag, Is.EqualTo(new GASNetworkTagId(37UL)));
        }

        [Test]
        public void StateBatch_RejectsLengthCapacityRecordAndMtuViolations()
        {
            var emptySnapshot = new GASStateBatchChunk(
                1u, 1u, new GASNetworkEntityId(1UL), GASStateBatchKind.Snapshot,
                0UL, 1UL, 0u, 0, 1, 0, 0, 0, 0, 0, 0, 3UL);
            Span<byte> headerBytes = stackalloc byte[GASNetworkWireCodec.StateBatchHeaderBytes];
            Assert.That(GASNetworkWireCodec.TryWriteStateBatchChunk(
                    in emptySnapshot,
                    ReadOnlySpan<GASAbilityStateRecord>.Empty,
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                    headerBytes,
                    out _),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(GASNetworkWireCodec.TryReadStateBatchChunk(
                    headerBytes.Slice(0, headerBytes.Length - 1),
                    Span<GASAbilityStateRecord>.Empty,
                    Span<GASAttributeStateRecord>.Empty,
                    Span<GASEffectStateRecord>.Empty,
                    Span<GASEffectTagStateRecord>.Empty,
                    Span<GASEffectMagnitudeStateRecord>.Empty,
                    Span<GASLooseTagStateRecord>.Empty,
                    out _),
                Is.EqualTo(GASNetworkWireCodecResult.InvalidPayloadLength));

            const int count = 20;
            var effects = new GASEffectStateRecord[count];
            for (int i = 0; i < effects.Length; i++)
            {
                effects[i] = new GASEffectStateRecord(
                    GASStateRecordOperation.Upsert, new GASNetworkEffectId((ulong)i + 1UL),
                    new GASNetworkContentId(1UL), default, 0u, default, 1, 1, 1L, 1L, 0L, 0u, default);
            }
            var oversized = new GASStateBatchChunk(
                1u, 2u, new GASNetworkEntityId(1UL), GASStateBatchKind.Snapshot,
                0UL, 2UL, 0u, 0, 1, 0, 0, count, 0, 0, 0, 5UL);
            var mtu = new byte[GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteStateBatchChunk(
                    in oversized,
                    ReadOnlySpan<GASAbilityStateRecord>.Empty,
                    ReadOnlySpan<GASAttributeStateRecord>.Empty,
                    effects,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                    mtu,
                    out _),
                Is.EqualTo(GASNetworkWireCodecResult.PayloadTooLarge));
        }

        [Test]
        public void AckResyncAndCue_RoundTripAndFailClosed()
        {
            var acknowledgement = new GASStateAcknowledgement(2u, 3u, new GASNetworkEntityId(5UL), 7UL, 11UL);
            Span<byte> ackBytes = stackalloc byte[GASNetworkWireCodec.StateAcknowledgementPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteStateAcknowledgement(in acknowledgement, ackBytes, out _), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(GASNetworkWireCodec.TryReadStateAcknowledgement(ackBytes, out GASStateAcknowledgement decodedAck), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(decodedAck.StateChecksum, Is.EqualTo(11UL));

            var resync = new GASResyncRequest(2u, 4u, new GASNetworkEntityId(5UL), 7UL, 8u, 11UL, GASResyncReason.ChecksumMismatch);
            Span<byte> resyncBytes = stackalloc byte[GASNetworkWireCodec.ResyncRequestPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteResyncRequest(in resync, resyncBytes, out _), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(GASNetworkWireCodec.TryReadResyncRequest(resyncBytes, out GASResyncRequest decodedResync), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(decodedResync.Reason, Is.EqualTo(GASResyncReason.ChecksumMismatch));

            var cue = new GASCueExecuted(
                2u, 5u, new GASNetworkEntityId(5UL), new GASNetworkTagId(13UL),
                new GASNetworkEntityId(17UL), new GASNetworkEffectId(19UL), 3u, 7UL,
                GASCueEvent.Execute, GASCueFlags.HasLocation | GASCueFlags.HasNormal,
                2.5f, new GASNetworkVector3(1f, 2f, 3f), new GASNetworkVector3(0f, 1f, 0f));
            Span<byte> cueBytes = stackalloc byte[GASNetworkWireCodec.CueExecutedPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteCueExecuted(in cue, cueBytes, out _), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(GASNetworkWireCodec.TryReadCueExecuted(cueBytes, out GASCueExecuted decodedCue), Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(decodedCue.Cue, Is.EqualTo(new GASNetworkTagId(13UL)));
            Assert.That(decodedCue.Location, Is.EqualTo(cue.Location));

            resyncBytes[37] = byte.MaxValue;
            Assert.That(GASNetworkWireCodec.TryReadResyncRequest(resyncBytes, out _), Is.EqualTo(GASNetworkWireCodecResult.MalformedMessage));
            Assert.That(GASNetworkWireCodec.TryReadCueExecuted(cueBytes.Slice(0, cueBytes.Length - 1), out _),
                Is.EqualTo(GASNetworkWireCodecResult.InvalidPayloadLength));
        }

        [Test]
        public void DefaultIdentitiesMessagesAndResults_FailClosed()
        {
            Assert.That(default(GASNetworkEntityId).IsValid, Is.False);
            Assert.That(default(GASNetworkGrantId).IsValid, Is.False);
            Assert.That(default(GASNetworkEffectId).IsValid, Is.False);
            Assert.That(default(GASNetworkContentId).IsValid, Is.False);
            Assert.That(default(GASNetworkTagId).IsValid, Is.False);
            Assert.That(default(GASAbilityCommand).IsHeaderValid, Is.False);
            Assert.That(default(GASCommandResult).IsValid, Is.False);
            Assert.That(default(GASStateBatchChunk).IsValid, Is.False);
            Assert.That(default(GASStateAcknowledgement).IsValid, Is.False);
            Assert.That(default(GASResyncRequest).IsValid, Is.False);
            Assert.That(default(GASCueExecuted).IsValid, Is.False);
            Assert.That(default(GASNetworkWireCodecResult), Is.EqualTo(GASNetworkWireCodecResult.Invalid));
        }

        [Test]
        public void WarmFixedCodecPath_AllocatesNoManagedMemoryOnEditorMono()
        {
            var command = new GASAbilityCommand(
                2u, 3u, new GASNetworkEntityId(5UL), new GASNetworkGrantId(7UL), GASAbilityCommandKind.Activate);
            var result = new GASCommandResult(
                2u, 3u, new GASNetworkEntityId(5UL), new GASNetworkGrantId(7UL),
                GASAbilityCommandKind.Activate, GASCommandStatus.Accepted, 9UL);
            Span<byte> commandBytes = stackalloc byte[GASNetworkWireCodec.AbilityCommandHeaderBytes];
            Span<byte> resultBytes = stackalloc byte[GASNetworkWireCodec.CommandResultPayloadBytes];

            int checksum = 0;
            for (int i = 0; i < 1_000; i++)
                checksum += ExerciseFixedCodec(in command, in result, commandBytes, resultBytes);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100_000; i++)
                checksum += ExerciseFixedCodec(in command, in result, commandBytes, resultBytes);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(checksum, Is.GreaterThan(0));
            Assert.That(allocated, Is.Zero);
        }

        private static int ExerciseFixedCodec(
            in GASAbilityCommand command,
            in GASCommandResult result,
            Span<byte> commandBytes,
            Span<byte> resultBytes)
        {
            int checksum = (int)GASNetworkWireCodec.TryWriteAbilityCommand(
                in command, ReadOnlySpan<GASNetworkEntityId>.Empty, commandBytes, out int commandWritten);
            checksum += commandWritten;
            checksum += (int)GASNetworkWireCodec.TryReadAbilityCommand(
                commandBytes, Span<GASNetworkEntityId>.Empty, out GASAbilityCommand decodedCommand, out _);
            checksum += decodedCommand.IsHeaderValid ? 1 : 0;
            checksum += GASNetworkWireCodec.ComputeAbilityCommandFingerprint(
                in command,
                ReadOnlySpan<GASNetworkEntityId>.Empty) != 0UL ? 1 : 0;
            checksum += (int)GASNetworkWireCodec.TryWriteCommandResult(in result, resultBytes, out int resultWritten);
            checksum += resultWritten;
            checksum += (int)GASNetworkWireCodec.TryReadCommandResult(resultBytes, out GASCommandResult decodedResult);
            checksum += decodedResult.IsValid ? 1 : 0;
            return checksum;
        }
    }
}
