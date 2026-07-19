using System;
using System.Collections.Generic;
using CycloneGames.Networking;
using CycloneGames.Networking.Security;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASNetworkEndpointTests
    {
        private const ulong ContentHash = 0x1122334455667788UL;
        private const ulong TagHash = 0x8877665544332211UL;

        [Test]
        public void Client_RegistersSevenHandlersAndGatesExactCommandBytesBehindHandshake()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new ClientSink();
            var failures = new FailureRecorder();
            var authority = new FakeConnection(10);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record);

            Assert.That(wire.HandlerCount, Is.EqualTo(7));

            GASNetworkEntityId[] targets =
            {
                new GASNetworkEntityId(101UL),
                new GASNetworkEntityId(102UL)
            };
            var command = new GASAbilityCommand(
                3u,
                4u,
                new GASNetworkEntityId(5UL),
                new GASNetworkGrantId(6UL),
                GASAbilityCommandKind.ConfirmTarget,
                GASTargetDataKind.ActorList,
                (byte)targets.Length);

            NetworkSendResult gated = endpoint.SendAbilityCommand(in command, targets);
            Assert.That(gated.Status, Is.EqualTo(NetworkSendStatus.NotConnected));
            Assert.That(wire.SendCount, Is.Zero);

            NetworkSendResult handshakeSend = endpoint.SendHandshakeToAuthority();
            Assert.That(handshakeSend.Succeeded, Is.True);
            Assert.That(wire.LastRoute, Is.EqualTo(FakeSendRoute.Server));
            Assert.That(wire.LastMessageId, Is.EqualTo(GameplayAbilitiesNetworkProtocol.HandshakeMessageId));
            CollectionAssert.AreEqual(EncodeHandshake(ContentHash, TagHash), wire.LastPayload);

            Assert.That(wire.Dispatch(
                authority,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ServerToClient), Is.True);
            Assert.That(endpoint.IsAuthorityHandshakeComplete, Is.True);
            Assert.That(sink.AuthorityReadyCount, Is.EqualTo(1));
            Assert.That(sink.LastAuthority, Is.SameAs(authority));

            Assert.That(wire.Dispatch(
                authority,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ServerToClient), Is.True);
            Assert.That(sink.AuthorityReadyCount, Is.EqualTo(1));

            wire.ClearSendCapture();
            NetworkSendResult sent = endpoint.SendAbilityCommand(in command, targets);
            Assert.That(sent.Succeeded, Is.True);
            Assert.That(wire.LastRoute, Is.EqualTo(FakeSendRoute.Server));
            Assert.That(wire.LastMessageId, Is.EqualTo(GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId));
            Assert.That(wire.LastChannel, Is.EqualTo(NetworkChannel.Reliable));

            var expected = new byte[GASNetworkWireCodec.MaxAbilityCommandPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteAbilityCommand(
                    in command,
                    targets,
                    expected,
                    out int expectedLength),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            CollectionAssert.AreEqual(expected.AsSpan(0, expectedLength).ToArray(), wire.LastPayload);

            wire.ClearSendCapture();
            var exhaustedSequence = new GASAbilityCommand(
                command.StreamEpoch,
                (uint)GameplayAbilitiesNetworkProtocol.MaxSequence + 1u,
                command.Entity,
                command.Grant,
                command.Kind);
            Assert.That(
                endpoint.SendAbilityCommand(
                    in exhaustedSequence,
                    ReadOnlySpan<GASNetworkEntityId>.Empty).Status,
                Is.EqualTo(NetworkSendStatus.InvalidPayload));
            Assert.That(wire.SendCount, Is.Zero);
            Assert.That(failures.Count, Is.Zero);
        }

        [Test]
        public void Authority_HandshakeGateDecodesActorTargetsAndRejectsWrongDirection()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new AuthoritySink();
            var failures = new FailureRecorder();
            var client = new FakeConnection(20);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record,
                disconnectOnProtocolViolation: false);

            GASNetworkEntityId[] targets =
            {
                new GASNetworkEntityId(201UL),
                new GASNetworkEntityId(202UL),
                new GASNetworkEntityId(203UL)
            };
            var command = new GASAbilityCommand(
                2u,
                9u,
                new GASNetworkEntityId(11UL),
                new GASNetworkGrantId(12UL),
                GASAbilityCommandKind.ConfirmTarget,
                GASTargetDataKind.ActorList,
                (byte)targets.Length);
            byte[] commandBytes = EncodeAbilityCommand(in command, targets);

            Assert.That(wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                commandBytes,
                NetworkMessageDirection.ClientToServer), Is.True);
            Assert.That(sink.CommandCount, Is.Zero);
            Assert.That(failures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.UnknownPeer));

            Assert.That(wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ClientToServer), Is.True);
            Assert.That(endpoint.IsClientHandshakeComplete(client), Is.True);
            Assert.That(sink.ClientReadyCount, Is.EqualTo(1));
            Assert.That(sink.LastClient, Is.SameAs(client));
            Assert.That(wire.LastRoute, Is.EqualTo(FakeSendRoute.Client));
            Assert.That(wire.LastConnection, Is.SameAs(client));
            CollectionAssert.AreEqual(EncodeHandshake(ContentHash, TagHash), wire.LastPayload);

            Assert.That(wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                commandBytes,
                NetworkMessageDirection.ClientToServer), Is.True);
            Assert.That(sink.CommandCount, Is.EqualTo(1));
            Assert.That(sink.LastCommand.CommandSequence, Is.EqualTo(command.CommandSequence));
            Assert.That(sink.LastActorTargetCount, Is.EqualTo(targets.Length));
            Assert.That(sink.FirstActorTarget, Is.EqualTo(targets[0]));

            Assert.That(wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                commandBytes,
                NetworkMessageDirection.ServerToClient), Is.True);
            Assert.That(sink.CommandCount, Is.EqualTo(1));
            Assert.That(failures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.UnexpectedDirection));
            Assert.That(endpoint.IsClientHandshakeComplete(client), Is.False);
            Assert.That(sink.ClientDisconnectedCount, Is.EqualTo(1));

            wire.Lifecycle.RaiseClientDisconnected(client);
            Assert.That(sink.ClientDisconnectedCount, Is.EqualTo(1));
        }

        [Test]
        public void EntryFailureCallbackException_DoesNotPoisonLaterDispatch()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new AuthoritySink();
            var failures = new ThrowOnceFailureRecorder();
            var client = new FakeConnection(21);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record,
                disconnectOnProtocolViolation: false);
            var command = new GASAbilityCommand(
                2u,
                1u,
                new GASNetworkEntityId(31UL),
                new GASNetworkGrantId(32UL),
                GASAbilityCommandKind.Activate);
            byte[] commandBytes = EncodeAbilityCommand(
                in command,
                ReadOnlySpan<GASNetworkEntityId>.Empty);

            Assert.Throws<InvalidOperationException>(() => wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                commandBytes,
                NetworkMessageDirection.ClientToServer));
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.UnknownPeer));

            Assert.That(wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ClientToServer), Is.True);
            Assert.That(endpoint.IsClientHandshakeComplete(client), Is.True);
            Assert.That(wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                commandBytes,
                NetworkMessageDirection.ClientToServer), Is.True);
            Assert.That(sink.CommandCount, Is.EqualTo(1));
            Assert.That(failures.Count, Is.EqualTo(1));
        }

        [Test]
        public void IncompatibleHandshake_IsReportedAndNeverOpensGameplayGate()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new AuthoritySink();
            var failures = new FailureRecorder();
            var client = new FakeConnection(30);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record);

            Assert.That(wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash + 1UL, TagHash),
                NetworkMessageDirection.ClientToServer), Is.True);

            Assert.That(endpoint.IsClientHandshakeComplete(client), Is.False);
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.HandshakeRejected));
            Assert.That(failures.Last.HandshakeResult, Is.EqualTo(GASNetworkHandshakeResult.ContentCatalogMismatch));
            Assert.That(wire.DisconnectCount, Is.EqualTo(1));
            Assert.That(wire.LastDisconnected, Is.SameAs(client));
        }

        [Test]
        public void FixedRoleMessages_UseCanonicalDirectionAndExactBytes()
        {
            var clientWire = new FakeMessageEndpoint();
            var clientSink = new ClientSink();
            var clientFailures = new FailureRecorder();
            var authorityConnection = new FakeConnection(40);
            using var clientEndpoint = new GASNetworkEndpoint(
                clientWire,
                ContentHash,
                TagHash,
                clientSink,
                clientFailures.Record);
            EstablishClient(clientEndpoint, clientWire, authorityConnection);

            var acknowledgement = new GASStateAcknowledgement(
                1u,
                2u,
                new GASNetworkEntityId(3UL),
                4UL,
                5UL);
            clientWire.ClearSendCapture();
            Assert.That(clientEndpoint.SendStateAcknowledgement(in acknowledgement).Succeeded, Is.True);
            AssertSendMatches(
                clientWire,
                FakeSendRoute.Server,
                GameplayAbilitiesNetworkProtocol.StateAcknowledgementMessageId,
                EncodeStateAcknowledgement(in acknowledgement));

            var resync = new GASResyncRequest(
                1u,
                3u,
                new GASNetworkEntityId(3UL),
                4UL,
                5u,
                6UL,
                GASResyncReason.ChecksumMismatch);
            Assert.That(clientEndpoint.SendResyncRequest(in resync).Succeeded, Is.True);
            AssertSendMatches(
                clientWire,
                FakeSendRoute.Server,
                GameplayAbilitiesNetworkProtocol.ResyncRequestMessageId,
                EncodeResyncRequest(in resync));

            var authorityWire = new FakeMessageEndpoint();
            var authoritySink = new AuthoritySink();
            var authorityFailures = new FailureRecorder();
            var clientConnection = new FakeConnection(41);
            using var authorityEndpoint = new GASNetworkEndpoint(
                authorityWire,
                ContentHash,
                TagHash,
                authoritySink,
                authorityFailures.Record);
            EstablishAuthority(authorityEndpoint, authorityWire, clientConnection);

            var result = new GASCommandResult(
                1u,
                7u,
                new GASNetworkEntityId(3UL),
                new GASNetworkGrantId(8UL),
                GASAbilityCommandKind.Activate,
                GASCommandStatus.Accepted,
                9UL);
            authorityWire.ClearSendCapture();
            Assert.That(authorityEndpoint.SendCommandResult(clientConnection, in result).Succeeded, Is.True);
            AssertSendMatches(
                authorityWire,
                FakeSendRoute.Client,
                GameplayAbilitiesNetworkProtocol.CommandResultMessageId,
                EncodeCommandResult(in result));

            var cue = CreateCue();
            Assert.That(authorityEndpoint.SendCueExecuted(clientConnection, in cue).Succeeded, Is.True);
            AssertSendMatches(
                authorityWire,
                FakeSendRoute.Client,
                GameplayAbilitiesNetworkProtocol.CueExecutedMessageId,
                EncodeCue(in cue));

            IReadOnlyList<INetConnection> recipients = new INetConnection[] { clientConnection };
            Assert.That(authorityEndpoint.BroadcastCueExecuted(recipients, in cue).Succeeded, Is.True);
            AssertSendMatches(
                authorityWire,
                FakeSendRoute.ExplicitBroadcast,
                GameplayAbilitiesNetworkProtocol.CueExecutedMessageId,
                EncodeCue(in cue));
        }

        [Test]
        public void StateChunkSend_RequiresEndpointBudgetAndCallerOwnedScratch()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new AuthoritySink();
            var failures = new FailureRecorder();
            var client = new FakeConnection(50);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record);
            EstablishAuthority(endpoint, wire, client);
            wire.ClearSendCapture();

            var emptyDelta = new GASStateBatchChunk(
                1u,
                1u,
                new GASNetworkEntityId(3UL),
                GASStateBatchKind.Delta,
                1UL,
                2UL,
                0u,
                0,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                5UL);
            var emptyDeltaScratch = new byte[GASNetworkWireCodec.StateBatchHeaderBytes];
            NetworkSendResult emptyDeltaFailure = endpoint.SendStateBatchChunk(
                client,
                in emptyDelta,
                ReadOnlySpan<GASAbilityStateRecord>.Empty,
                ReadOnlySpan<GASAttributeStateRecord>.Empty,
                ReadOnlySpan<GASEffectStateRecord>.Empty,
                ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                emptyDeltaScratch);
            Assert.That(emptyDeltaFailure.Status, Is.EqualTo(NetworkSendStatus.InvalidPayload));
            Assert.That(wire.SendCount, Is.Zero);

            GASStateBatchChunk header = CreateAttributeSnapshot(out GASAttributeStateRecord[] attributes);
            int required = GASNetworkWireCodec.GetStateBatchPayloadBytes(in header);
            wire.BudgetMessageId = GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId;
            wire.Budget = required - 1;
            var scratch = new byte[required];

            NetworkSendResult budgetFailure = endpoint.SendStateBatchChunk(
                client,
                in header,
                ReadOnlySpan<GASAbilityStateRecord>.Empty,
                attributes,
                ReadOnlySpan<GASEffectStateRecord>.Empty,
                ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                scratch);
            Assert.That(budgetFailure.Status, Is.EqualTo(NetworkSendStatus.PayloadTooLarge));
            Assert.That(wire.SendCount, Is.Zero);

            wire.Budget = required;
            NetworkSendResult scratchFailure = endpoint.SendStateBatchChunk(
                client,
                in header,
                ReadOnlySpan<GASAbilityStateRecord>.Empty,
                attributes,
                ReadOnlySpan<GASEffectStateRecord>.Empty,
                ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                scratch.AsSpan(0, required - 1));
            Assert.That(scratchFailure.Status, Is.EqualTo(NetworkSendStatus.InvalidPayload));
            Assert.That(wire.SendCount, Is.Zero);

            NetworkSendResult sent = endpoint.SendStateBatchChunk(
                client,
                in header,
                ReadOnlySpan<GASAbilityStateRecord>.Empty,
                attributes,
                ReadOnlySpan<GASEffectStateRecord>.Empty,
                ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                scratch);
            Assert.That(sent.Succeeded, Is.True);

            var expected = new byte[required];
            Assert.That(GASNetworkWireCodec.TryWriteStateBatchChunk(
                    in header,
                    ReadOnlySpan<GASAbilityStateRecord>.Empty,
                    attributes,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                    expected,
                    out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(required));
            AssertSendMatches(
                wire,
                FakeSendRoute.Client,
                GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId,
                expected);
        }

        [Test]
        public void StateChunkReceive_UsesBoundedScratchAndMalformedPayloadFailsClosed()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new ClientSink();
            var failures = new FailureRecorder();
            var authority = new FakeConnection(60);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record);
            EstablishClient(endpoint, wire, authority);

            GASStateBatchChunk header = CreateAttributeSnapshot(out GASAttributeStateRecord[] attributes);
            byte[] payload = EncodeStateChunk(in header, attributes);
            Assert.That(wire.Dispatch(
                authority,
                GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId,
                payload,
                NetworkMessageDirection.ServerToClient), Is.True);
            Assert.That(sink.StateChunkCount, Is.EqualTo(1));
            Assert.That(sink.LastStateVersion, Is.EqualTo(header.StateVersion));
            Assert.That(sink.LastAttributeCount, Is.EqualTo(1));
            Assert.That(sink.FirstAttributeCurrentValue, Is.EqualTo(attributes[0].CurrentValueRaw));

            Assert.That(wire.Dispatch(
                authority,
                GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId,
                payload.AsSpan(0, payload.Length - 1),
                NetworkMessageDirection.ServerToClient), Is.True);
            Assert.That(sink.StateChunkCount, Is.EqualTo(1));
            Assert.That(failures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.MalformedPayload));
            Assert.That(failures.Last.CodecResult, Is.EqualTo(GASNetworkWireCodecResult.InvalidPayloadLength));
            Assert.That(endpoint.IsAuthorityHandshakeComplete, Is.False);
            Assert.That(wire.DisconnectCount, Is.EqualTo(1));
            Assert.That(sink.AuthorityDisconnectedCount, Is.EqualTo(1));

            wire.Lifecycle.RaiseDisconnectedFromServer();
            Assert.That(sink.AuthorityDisconnectedCount, Is.EqualTo(1));
        }

        [Test]
        public void SinkException_IsReportedInvalidatesPeerAndDisconnects()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new AuthoritySink { ThrowOnCommand = true };
            var failures = new FailureRecorder();
            var client = new FakeConnection(70);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record);
            EstablishAuthority(endpoint, wire, client);

            var command = new GASAbilityCommand(
                1u,
                2u,
                new GASNetworkEntityId(3UL),
                new GASNetworkGrantId(4UL),
                GASAbilityCommandKind.Activate);
            byte[] bytes = EncodeAbilityCommand(in command, ReadOnlySpan<GASNetworkEntityId>.Empty);
            Assert.DoesNotThrow(() => wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                bytes,
                NetworkMessageDirection.ClientToServer));

            Assert.That(failures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.MessageHandlerException));
            Assert.That(failures.Last.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(endpoint.IsClientHandshakeComplete(client), Is.False);
            Assert.That(wire.DisconnectCount, Is.EqualTo(1));
            Assert.That(sink.ClientDisconnectedCount, Is.EqualTo(1));

            wire.Lifecycle.RaiseClientDisconnected(client);
            Assert.That(sink.ClientDisconnectedCount, Is.EqualTo(1));
        }

        [Test]
        public void ReadyCallbackException_IsReportedAndDisconnectsExactlyOnce()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new AuthoritySink { ThrowOnReady = true };
            var failures = new FailureRecorder();
            var client = new FakeConnection(71);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record);

            Assert.DoesNotThrow(() => wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ClientToServer));

            Assert.That(sink.ClientReadyCount, Is.EqualTo(1));
            Assert.That(sink.ClientDisconnectedCount, Is.EqualTo(1));
            Assert.That(endpoint.IsClientHandshakeComplete(client), Is.False);
            Assert.That(wire.DisconnectCount, Is.EqualTo(1));
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.MessageHandlerException));
            Assert.That(failures.Last.Exception, Is.TypeOf<InvalidOperationException>());

            wire.Lifecycle.RaiseClientDisconnected(client);
            Assert.That(sink.ClientDisconnectedCount, Is.EqualTo(1));
        }

        [Test]
        public void PublicHandshakeRetry_CompletesPartialClientAndAuthoritySessionsOnce()
        {
            var clientWire = new FakeMessageEndpoint
            {
                NextSendStatus = NetworkSendStatus.Backpressure
            };
            var clientSink = new ClientSink();
            var clientFailures = new FailureRecorder();
            var authority = new FakeConnection(72);
            using var clientEndpoint = new GASNetworkEndpoint(
                clientWire,
                ContentHash,
                TagHash,
                clientSink,
                clientFailures.Record);

            Assert.That(clientWire.Dispatch(
                authority,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ServerToClient), Is.True);
            Assert.That(clientEndpoint.IsAuthorityHandshakeComplete, Is.False);
            Assert.That(clientSink.AuthorityReadyCount, Is.Zero);
            Assert.That(clientFailures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.HandshakeResponseFailed));

            Assert.That(clientEndpoint.SendHandshakeToAuthority().Succeeded, Is.True);
            Assert.That(clientEndpoint.IsAuthorityHandshakeComplete, Is.True);
            Assert.That(clientSink.AuthorityReadyCount, Is.EqualTo(1));
            Assert.That(clientEndpoint.SendHandshakeToAuthority().Succeeded, Is.True);
            Assert.That(clientSink.AuthorityReadyCount, Is.EqualTo(1));

            var authorityWire = new FakeMessageEndpoint
            {
                NextSendStatus = NetworkSendStatus.Backpressure
            };
            var authoritySink = new AuthoritySink();
            var authorityFailures = new FailureRecorder();
            var client = new FakeConnection(73);
            using var authorityEndpoint = new GASNetworkEndpoint(
                authorityWire,
                ContentHash,
                TagHash,
                authoritySink,
                authorityFailures.Record);

            Assert.That(authorityWire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ClientToServer), Is.True);
            Assert.That(authorityEndpoint.IsClientHandshakeComplete(client), Is.False);
            Assert.That(authoritySink.ClientReadyCount, Is.Zero);
            Assert.That(authorityFailures.Last.Kind, Is.EqualTo(GASNetworkEndpointFailureKind.HandshakeResponseFailed));

            Assert.That(authorityEndpoint.SendHandshakeToClient(client).Succeeded, Is.True);
            Assert.That(authorityEndpoint.IsClientHandshakeComplete(client), Is.True);
            Assert.That(authoritySink.ClientReadyCount, Is.EqualTo(1));
            Assert.That(authorityEndpoint.SendHandshakeToClient(client).Succeeded, Is.True);
            Assert.That(authoritySink.ClientReadyCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_ReleasesHandlersAndRepeatedDisposeCannotRemoveReplacement()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new ClientSink();
            var failures = new FailureRecorder();
            var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record);
            byte[] handshake = EncodeHandshake(ContentHash, TagHash);
            var authority = new FakeConnection(80);

            Assert.That(wire.HandlerCount, Is.EqualTo(7));
            EstablishClient(endpoint, wire, authority);
            Assert.That(sink.AuthorityReadyCount, Is.EqualTo(1));
            endpoint.Dispose();
            Assert.That(wire.HandlerCount, Is.Zero);
            Assert.That(sink.AuthorityDisconnectedCount, Is.EqualTo(1));
            Assert.That(wire.Dispatch(
                authority,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                handshake,
                NetworkMessageDirection.ServerToClient), Is.False);

            int replacementCalls = 0;
            NetworkMessageHandlerLease replacement = wire.RegisterHandler(
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                (in NetworkMessagePayload _) => replacementCalls++);
            endpoint.Dispose();
            Assert.That(wire.Dispatch(
                authority,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                handshake,
                NetworkMessageDirection.ServerToClient), Is.True);
            Assert.That(replacementCalls, Is.EqualTo(1));
            wire.Lifecycle.RaiseDisconnectedFromServer();
            Assert.That(sink.AuthorityDisconnectedCount, Is.EqualTo(1));
            replacement.Dispose();
        }

        [Test]
        public void WarmAuthorityDispatch_AllocatesNoManagedMemoryOnEditorMono()
        {
            var wire = new FakeMessageEndpoint();
            var sink = new AuthoritySink();
            var failures = new FailureRecorder();
            var client = new FakeConnection(90);
            using var endpoint = new GASNetworkEndpoint(
                wire,
                ContentHash,
                TagHash,
                sink,
                failures.Record);
            EstablishAuthority(endpoint, wire, client);

            var command = new GASAbilityCommand(
                1u,
                2u,
                new GASNetworkEntityId(3UL),
                new GASNetworkGrantId(4UL),
                GASAbilityCommandKind.Activate);
            byte[] bytes = EncodeAbilityCommand(in command, ReadOnlySpan<GASNetworkEntityId>.Empty);

            for (int i = 0; i < 1_000; i++)
            {
                wire.Dispatch(
                    client,
                    GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                    bytes,
                    NetworkMessageDirection.ClientToServer);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100_000; i++)
            {
                wire.Dispatch(
                    client,
                    GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                    bytes,
                    NetworkMessageDirection.ClientToServer);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(sink.CommandCount, Is.EqualTo(101_000));
            Assert.That(failures.Count, Is.Zero);
            Assert.That(allocated, Is.Zero);
        }

        private static void EstablishClient(
            GASNetworkEndpoint endpoint,
            FakeMessageEndpoint wire,
            INetConnection authority)
        {
            Assert.That(endpoint.SendHandshakeToAuthority().Succeeded, Is.True);
            Assert.That(wire.Dispatch(
                authority,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ServerToClient), Is.True);
            Assert.That(endpoint.IsAuthorityHandshakeComplete, Is.True);
        }

        private static void EstablishAuthority(
            GASNetworkEndpoint endpoint,
            FakeMessageEndpoint wire,
            INetConnection client)
        {
            Assert.That(wire.Dispatch(
                client,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                EncodeHandshake(ContentHash, TagHash),
                NetworkMessageDirection.ClientToServer), Is.True);
            Assert.That(endpoint.IsClientHandshakeComplete(client), Is.True);
        }

        private static byte[] EncodeHandshake(ulong contentHash, ulong tagHash)
        {
            GASNetworkHandshake handshake = GameplayAbilitiesNetworkProtocol.CreateHandshake(contentHash, tagHash);
            var bytes = new byte[GASNetworkWireCodec.HandshakePayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteHandshake(in handshake, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            return bytes;
        }

        private static byte[] EncodeAbilityCommand(
            in GASAbilityCommand message,
            ReadOnlySpan<GASNetworkEntityId> targets)
        {
            var scratch = new byte[GASNetworkWireCodec.MaxAbilityCommandPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteAbilityCommand(
                    in message,
                    targets,
                    scratch,
                    out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            return scratch.AsSpan(0, written).ToArray();
        }

        private static byte[] EncodeCommandResult(in GASCommandResult message)
        {
            var bytes = new byte[GASNetworkWireCodec.CommandResultPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteCommandResult(in message, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            return bytes;
        }

        private static byte[] EncodeStateAcknowledgement(in GASStateAcknowledgement message)
        {
            var bytes = new byte[GASNetworkWireCodec.StateAcknowledgementPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteStateAcknowledgement(in message, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            return bytes;
        }

        private static byte[] EncodeResyncRequest(in GASResyncRequest message)
        {
            var bytes = new byte[GASNetworkWireCodec.ResyncRequestPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteResyncRequest(in message, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            return bytes;
        }

        private static byte[] EncodeCue(in GASCueExecuted message)
        {
            var bytes = new byte[GASNetworkWireCodec.CueExecutedPayloadBytes];
            Assert.That(GASNetworkWireCodec.TryWriteCueExecuted(in message, bytes, out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            return bytes;
        }

        private static byte[] EncodeStateChunk(
            in GASStateBatchChunk header,
            ReadOnlySpan<GASAttributeStateRecord> attributes)
        {
            int required = GASNetworkWireCodec.GetStateBatchPayloadBytes(in header);
            var bytes = new byte[required];
            Assert.That(GASNetworkWireCodec.TryWriteStateBatchChunk(
                    in header,
                    ReadOnlySpan<GASAbilityStateRecord>.Empty,
                    attributes,
                    ReadOnlySpan<GASEffectStateRecord>.Empty,
                    ReadOnlySpan<GASEffectTagStateRecord>.Empty,
                    ReadOnlySpan<GASEffectMagnitudeStateRecord>.Empty,
                    ReadOnlySpan<GASLooseTagStateRecord>.Empty,
                    bytes,
                    out int written),
                Is.EqualTo(GASNetworkWireCodecResult.Success));
            Assert.That(written, Is.EqualTo(bytes.Length));
            return bytes;
        }

        private static GASStateBatchChunk CreateAttributeSnapshot(
            out GASAttributeStateRecord[] attributes)
        {
            attributes = new[]
            {
                new GASAttributeStateRecord(
                    GASStateRecordOperation.Upsert,
                    new GASNetworkContentId(21UL),
                    100L,
                    75L)
            };
            return new GASStateBatchChunk(
                1u,
                2u,
                new GASNetworkEntityId(3UL),
                GASStateBatchKind.Snapshot,
                0UL,
                4UL,
                5u,
                0,
                1,
                0,
                1,
                0,
                0,
                0,
                0,
                6UL);
        }

        private static GASCueExecuted CreateCue()
        {
            return new GASCueExecuted(
                1u,
                2u,
                new GASNetworkEntityId(3UL),
                new GASNetworkTagId(4UL),
                new GASNetworkEntityId(5UL),
                new GASNetworkEffectId(6UL),
                7u,
                8UL,
                GASCueEvent.Execute,
                GASCueFlags.HasLocation,
                1.5f,
                new GASNetworkVector3(1f, 2f, 3f),
                default);
        }

        private static void AssertSendMatches(
            FakeMessageEndpoint wire,
            FakeSendRoute route,
            ushort messageId,
            byte[] expected)
        {
            Assert.That(wire.LastRoute, Is.EqualTo(route));
            Assert.That(wire.LastMessageId, Is.EqualTo(messageId));
            Assert.That(wire.LastChannel, Is.EqualTo(NetworkChannel.Reliable));
            CollectionAssert.AreEqual(expected, wire.LastPayload);
        }

        private sealed class FailureRecorder
        {
            public int Count { get; private set; }
            public GASNetworkEndpointFailure Last { get; private set; }

            public void Record(in GASNetworkEndpointFailure failure)
            {
                Count++;
                Last = failure;
            }
        }

        private sealed class ThrowOnceFailureRecorder
        {
            public int Count { get; private set; }
            public GASNetworkEndpointFailure Last { get; private set; }

            public void Record(in GASNetworkEndpointFailure failure)
            {
                Count++;
                Last = failure;
                if (Count == 1)
                {
                    throw new InvalidOperationException("Injected failure callback exception.");
                }
            }
        }

        private sealed class ClientSink : IGASNetworkClientSink
        {
            public int AuthorityReadyCount { get; private set; }
            public int AuthorityDisconnectedCount { get; private set; }
            public int CommandResultCount { get; private set; }
            public int StateChunkCount { get; private set; }
            public int CueCount { get; private set; }
            public INetConnection LastAuthority { get; private set; }
            public ulong LastStateVersion { get; private set; }
            public int LastAttributeCount { get; private set; }
            public long FirstAttributeCurrentValue { get; private set; }

            public void OnAuthorityReady(INetConnection authority)
            {
                AuthorityReadyCount++;
                LastAuthority = authority;
            }

            public void OnAuthorityDisconnected(INetConnection authority)
            {
                AuthorityDisconnectedCount++;
                LastAuthority = authority;
            }

            public void OnCommandResult(INetConnection authority, in GASCommandResult message)
            {
                CommandResultCount++;
            }

            public void OnStateBatchChunk(
                INetConnection authority,
                in GASStateBatchChunk message,
                ReadOnlySpan<GASAbilityStateRecord> abilities,
                ReadOnlySpan<GASAttributeStateRecord> attributes,
                ReadOnlySpan<GASEffectStateRecord> effects,
                ReadOnlySpan<GASEffectTagStateRecord> effectTags,
                ReadOnlySpan<GASEffectMagnitudeStateRecord> effectMagnitudes,
                ReadOnlySpan<GASLooseTagStateRecord> looseTags)
            {
                StateChunkCount++;
                LastStateVersion = message.StateVersion;
                LastAttributeCount = attributes.Length;
                FirstAttributeCurrentValue = attributes.Length > 0 ? attributes[0].CurrentValueRaw : 0L;
            }

            public void OnCueExecuted(INetConnection authority, in GASCueExecuted message)
            {
                CueCount++;
            }
        }

        private sealed class AuthoritySink : IGASNetworkAuthoritySink
        {
            public bool ThrowOnCommand { get; set; }
            public bool ThrowOnReady { get; set; }
            public int ClientReadyCount { get; private set; }
            public int ClientDisconnectedCount { get; private set; }
            public int CommandCount { get; private set; }
            public int LastActorTargetCount { get; private set; }
            public INetConnection LastClient { get; private set; }
            public GASNetworkEntityId FirstActorTarget { get; private set; }
            public GASAbilityCommand LastCommand { get; private set; }

            public void OnClientReady(INetConnection client)
            {
                ClientReadyCount++;
                LastClient = client;
                if (ThrowOnReady)
                    throw new InvalidOperationException("Injected authority ready callback failure.");
            }

            public void OnClientDisconnected(INetConnection client)
            {
                ClientDisconnectedCount++;
                LastClient = client;
            }

            public void OnAbilityCommand(
                INetConnection client,
                in GASAbilityCommand message,
                ReadOnlySpan<GASNetworkEntityId> actorTargets)
            {
                if (ThrowOnCommand)
                    throw new InvalidOperationException("Injected authority sink failure.");

                CommandCount++;
                LastCommand = message;
                LastActorTargetCount = actorTargets.Length;
                FirstActorTarget = actorTargets.Length > 0 ? actorTargets[0] : default;
            }

            public void OnStateAcknowledgement(INetConnection client, in GASStateAcknowledgement message)
            {
            }

            public void OnResyncRequest(INetConnection client, in GASResyncRequest message)
            {
            }
        }

        private enum FakeSendRoute : byte
        {
            None = 0,
            Server = 1,
            Client = 2,
            ClientBroadcast = 3,
            ExplicitBroadcast = 4
        }

        private sealed class FakeMessageEndpoint : INetworkMessageEndpoint
        {
            private readonly NetworkMessageHandlerRegistry handlers = new NetworkMessageHandlerRegistry(7, 32);

            public FakeTransport Lifecycle { get; } = new FakeTransport();
            public INetTransport Transport => Lifecycle;
            public bool IsAcceptingMessages { get; set; } = true;
            public int DefaultBudget { get; set; } = GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes;
            public ushort BudgetMessageId { get; set; }
            public int Budget { get; set; }
            public NetworkSendStatus NextSendStatus { get; set; }
            public int HandlerCount => handlers.Count;
            public int SendCount { get; private set; }
            public int DisconnectCount { get; private set; }
            public INetConnection LastDisconnected { get; private set; }
            public FakeSendRoute LastRoute { get; private set; }
            public INetConnection LastConnection { get; private set; }
            public ushort LastMessageId { get; private set; }
            public NetworkChannel LastChannel { get; private set; }
            public byte[] LastPayload { get; private set; }

            public int GetMaxPayloadSize(ushort messageId, NetworkChannel channel)
            {
                return messageId == BudgetMessageId && Budget > 0 ? Budget : DefaultBudget;
            }

            public NetworkMessageHandlerLease RegisterHandler(
                ushort messageId,
                NetworkMessageHandler handler)
            {
                return handlers.Register(messageId, handler);
            }

            public NetworkSendResult SendToServer(
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable)
            {
                Capture(FakeSendRoute.Server, null, messageId, payload, channel);
                return CompleteSend(payload.Length, channel, null);
            }

            public NetworkSendResult SendToClient(
                INetConnection connection,
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable)
            {
                Capture(FakeSendRoute.Client, connection, messageId, payload, channel);
                return CompleteSend(payload.Length, channel, connection);
            }

            public NetworkSendResult BroadcastToClients(
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable)
            {
                Capture(FakeSendRoute.ClientBroadcast, null, messageId, payload, channel);
                if (NextSendStatus != NetworkSendStatus.Invalid)
                {
                    NetworkSendStatus status = NextSendStatus;
                    NextSendStatus = NetworkSendStatus.Invalid;
                    return NetworkSendResult.Broadcast(status, 0, 0, (int)channel);
                }
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, payload.Length, 1, (int)channel);
            }

            public NetworkSendResult Broadcast(
                IReadOnlyList<INetConnection> connections,
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable)
            {
                Capture(FakeSendRoute.ExplicitBroadcast, null, messageId, payload, channel);
                if (NextSendStatus != NetworkSendStatus.Invalid)
                {
                    NetworkSendStatus status = NextSendStatus;
                    NextSendStatus = NetworkSendStatus.Invalid;
                    return NetworkSendResult.Broadcast(status, 0, 0, (int)channel);
                }
                return NetworkSendResult.Broadcast(
                    NetworkSendStatus.Accepted,
                    payload.Length,
                    connections.Count,
                    (int)channel);
            }

            public void Disconnect(INetConnection connection)
            {
                DisconnectCount++;
                LastDisconnected = connection;
            }

            public bool Dispatch(
                INetConnection connection,
                ushort messageId,
                ReadOnlySpan<byte> bytes,
                NetworkMessageDirection direction,
                NetworkChannel channel = NetworkChannel.Reliable)
            {
                var header = new NetworkEnvelopeHeader(
                    messageId,
                    channel,
                    bytes.Length,
                    1u,
                    0u);
                var payload = new NetworkMessagePayload(connection, direction, in header, bytes);
                return handlers.TryDispatch(in payload);
            }

            public void ClearSendCapture()
            {
                SendCount = 0;
                LastRoute = FakeSendRoute.None;
                LastConnection = null;
                LastMessageId = 0;
                LastChannel = default;
                LastPayload = null;
            }

            private void Capture(
                FakeSendRoute route,
                INetConnection connection,
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel)
            {
                SendCount++;
                LastRoute = route;
                LastConnection = connection;
                LastMessageId = messageId;
                LastChannel = channel;
                LastPayload = payload.ToArray();
            }

            private NetworkSendResult CompleteSend(
                int payloadLength,
                NetworkChannel channel,
                INetConnection connection)
            {
                if (NextSendStatus == NetworkSendStatus.Invalid)
                    return NetworkSendResult.Accepted(payloadLength, (int)channel, connection);

                NetworkSendStatus status = NextSendStatus;
                NextSendStatus = NetworkSendStatus.Invalid;
                return NetworkSendResult.Fail(status, (int)channel, connection);
            }
        }

        private sealed class FakeTransport : INetTransport
        {
            public bool IsServer => true;
            public bool IsClient => true;
            public bool IsRunning => true;
            public bool IsEncrypted => false;
            public bool Available => true;
            public NetworkTransportCapabilities Capabilities => default;

            public event Action<INetConnection> OnClientConnected;
            public event Action<INetConnection> OnClientDisconnected;
            public event Action OnConnectedToServer;
            public event Action OnDisconnectedFromServer;
            public event Action<INetConnection, TransportError, string> OnError
            {
                add { }
                remove { }
            }

            public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived
            {
                add { }
                remove { }
            }

            public int GetChannelId(NetworkChannel channel)
            {
                return (int)channel;
            }

            public int GetMaxPacketSize(int channelId)
            {
                return GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes;
            }

            public NetworkStatistics GetStatistics()
            {
                return default;
            }

            public void StartServer()
            {
                OnClientConnected?.Invoke(null);
            }

            public void StartClient(string address)
            {
                OnConnectedToServer?.Invoke();
            }

            public void Stop()
            {
            }

            public void Disconnect(INetConnection connection)
            {
                OnClientDisconnected?.Invoke(connection);
            }

            public NetworkSendResult Send(
                INetConnection connection,
                in ArraySegment<byte> payload,
                int channelId)
            {
                return NetworkSendResult.Accepted(payload.Count, channelId, connection);
            }

            public NetworkSendResult Broadcast(
                IReadOnlyList<INetConnection> connections,
                in ArraySegment<byte> payload,
                int channelId)
            {
                return NetworkSendResult.Broadcast(
                    NetworkSendStatus.Accepted,
                    payload.Count,
                    connections.Count,
                    channelId);
            }

            public void RaiseClientDisconnected(INetConnection client)
            {
                OnClientDisconnected?.Invoke(client);
            }

            public void RaiseDisconnectedFromServer()
            {
                OnDisconnectedFromServer?.Invoke();
            }
        }

        private sealed class FakeConnection : INetConnection
        {
            public FakeConnection(int connectionId)
            {
                ConnectionId = connectionId;
            }

            public int ConnectionId { get; }
            public string RemoteAddress => "test";
            public bool IsConnected { get; set; } = true;
            public bool IsAuthenticated { get; set; } = true;
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Excellent;
            public double Jitter => 0d;
            public long BytesSent => 0L;
            public long BytesReceived => 0L;
            public ulong PlayerId { get; set; }

            public bool Equals(INetConnection other)
            {
                return other != null && ConnectionId == other.ConnectionId;
            }

            public override bool Equals(object obj)
            {
                return obj is INetConnection other && Equals(other);
            }

            public override int GetHashCode()
            {
                return ConnectionId;
            }
        }
    }
}
